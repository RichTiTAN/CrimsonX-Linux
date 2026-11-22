# Transports

CrimsonX reads share links using the v2rayN conventions (`type`, `path`, `host`, `sni`, `fp`, `alpn`, `serviceName`, `headerType`, `pbk`, `sid`, `spx`, `flow`, `fm`). Xray itself defines no link format, so these are the names every panel and client emits.

Stream settings are written as `method`. Configs that still carry the legacy `network` field keep working, because every reader accepts both.

## Supported transports

| Transport | Accepted | Notes |
|-----------|----------|-------|
| `raw` / `tcp` | yes | written as `raw`, with `headerType=http` for HTTP obfuscation |
| `ws` | yes | `path` + `host` |
| `grpc` | yes | `serviceName`, `authority`, `mode` |
| `httpupgrade` | yes | `path` + `host` |
| `xhttp` / `splithttp` | yes | both names are read; written as `xhttp` with `xhttpSettings` |
| `kcp` / `mkcp` | yes | `mtu`, `tti`, `uplinkCapacity`, `downlinkCapacity`, `cwndMultiplier`, `maxSendingWindow` |
| `masque` | yes | through the `masque://` scheme below |
| `h2`, `h3`, `http`, `quic` | no | removed from Xray; use `xhttp` with `alpn=h2` or `alpn=h3` |

Where each transport can be used:

- **Custom config** (raw JSON or a share link): everything above.
- **Custom exit node**: xray-only transports are allowed, but UDP transports are not.
- **Apps & Games custom proxy**: only transports that sing-box can dial, and only TCP ones.
- **UDP transports** (`kcp`, `masque` over HTTP/3, `xhttp` with `alpn=h3`): custom configs only, because chains and app rules hop through a local TCP proxy.

## MASQUE

`masque://` is a CrimsonX extension: neither Xray nor v2rayN defines a MASQUE share link, so other clients will not understand it. The JSON it produces is the standard Xray MASQUE outbound.

```text
masque://user:pass@host:443?path=<uri-template>&host=<authority>&sni=<name>&alpn=h2|h3&fp=<fingerprint>&rdns=<ip,ip>#label
```

Working sample, verified with the bundled Xray (`xray -test` returns OK):

```text
masque://love:secret@example.com:443?path=%2F.well-known%2Fmasque%2Fip%2F%7Btarget%7D%2F%7Bipproto%7D%2F&host=example.com&sni=example.com&alpn=h2&rdns=1.1.1.1,2606:4700:4700::1111&fp=chrome#MASQUE-H2-Demo
```

```json
{
  "protocol": "masque",
  "settings": {
    "address": "example.com",
    "port": 443,
    "remoteDNS": ["1.1.1.1", "2606:4700:4700::1111"]
  },
  "streamSettings": {
    "method": "masque",
    "masqueSettings": {
      "host": "example.com",
      "path": "/.well-known/masque/ip/{target}/{ipproto}/",
      "user": "love",
      "pass": "secret"
    },
    "security": "tls",
    "tlsSettings": {
      "serverName": "example.com",
      "alpn": ["h2"],
      "fingerprint": "chrome"
    }
  }
}
```

- `path` defaults to `/.well-known/masque/ip/{target}/{ipproto}/` in Xray when it is omitted.
- `alpn=h2` carries MASQUE over HTTP/2 (TCP); anything else, including the default, uses HTTP/3 (UDP).
- TLS is mandatory and REALITY is not supported, so the parser always writes `security: tls`.
- `rdns` fills `settings.remoteDNS`, which takes IP addresses only.
- `masqueSettings.host` is the `:authority` of the CONNECT-IP request; without it Xray falls back to `tlsSettings.serverName` and then to the outbound address.
- For non-basic authentication use `masqueSettings.headers` (`"Authorization": "Bearer ..."`); it cannot be combined with `user`/`pass`.
- WARP-over-MASQUE is raw JSON only: add `masqueSettings.warp` with the WireGuard keys and the server-assigned addresses.

## WireGuard

Links use `wireguard://`, or the `wg://` alias. The private key sits in the userinfo, everything else in the query.

```text
wireguard://<private-key>@host:51820?publickey=<peer-public-key>&address=10.0.0.2/32&allowedips=0.0.0.0/0&keepalive=25#wg-demo
```

Accepted query keys: `publickey`/`pk`, `address`/`ip`, `allowedips`, `psk`/`presharedkey`, `keepalive`/`persistentkeepalive`, `reserved` (written as `a+b+c`), `mtu`, `listenport`, `workers`, plus the AmneziaWG options handled by the AmneziaWG builder.

```json
{
  "type": "wireguard",
  "private_key": "<private-key>",
  "address": ["10.0.0.2/32"],
  "peers": [
    {
      "address": "example.com",
      "port": 51820,
      "public_key": "<peer-public-key>",
      "allowed_ips": ["0.0.0.0/0", "::/0"],
      "persistent_keepalive_interval": 25
    }
  ],
  "mtu": 1420
}
```

- A `.conf` file and Xray's `protocol: wireguard` outbound are accepted too; all three normalize to the endpoint above.
- `allowed_ips` defaults to `0.0.0.0/0, ::/0` when the link omits it.
- `listen_port` and `workers` are optional and only added when the link carries them.

## IKEv2

```text
ikev2://user:pass@host:500?psk=<shared-secret>&remote_id=<id>&local_id=<id>&dns=1.1.1.1&mtu=1400#ikev2-demo
```

Accepted query keys: `psk`/`pre_shared_key`/`shared_secret`, `auth`/`auth_method`/`method` (`eap-mschapv2` or `psk`), `remote_id`/`remote_identifier`/`idr`, `local_id`/`local_identifier`/`idi`, `ca`/`ca_pem` (PEM text or base64), `dns`, `mtu`, `port`/`server_port`, `name`/`label`/`tag`.

```json
{
  "type": "ipsec",
  "server": "example.com",
  "server_port": 500,
  "username": "user",
  "password": "pass",
  "psk": "<shared-secret>",
  "remote_id": "vpn.example.com",
  "dns": "1.1.1.1",
  "mtu": 1400
}
```

- EAP-TLS client certificates are not supported; use a username and password (EAP-MSCHAPv2) or a pre-shared key.
- A `.mobileconfig` profile is accepted as well and normalizes to the same shape.

## OpenVPN

There is no OpenVPN URI scheme: paste the `.ovpn` text, including its inline blocks (`<ca>`, `<cert>`, `<key>`, `<tls-auth>`, `<tls-crypt>`, `<tls-crypt-v2>`, `<auth-user-pass>`).

```json
{
  "type": "openvpn-client",
  "mode": "tls",
  "network": "udp",
  "servers": [ { "server": "example.com", "server_port": 1194 } ],
  "tls": { "ca": "<pem>", "cert": "<pem>", "key": "<pem>" },
  "username": "user",
  "password": "pass"
}
```

- Static key mode uses `"mode": "static_key"` with `static_key`, `address` and optionally `peer_address`, and requires an inline `<secret>` block plus an `ifconfig` line.
- TAP (layer 2) configs are refused; use a TUN config.
- Static key mode supports a single remote; extra `remote` lines are dropped.
- `auth_retry` is carried through when the profile sets it.
