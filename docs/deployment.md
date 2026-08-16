# Deployment

The image builds the client and the API together and serves both from one
origin, so the session cookie has a single origin to belong to and no gateway
is needed to put the two behind one host.

```sh
docker build --tag sleeky-todo .
```

Development is unaffected: the API carries no `wwwroot` when it runs from
source, so requests for the client are answered 404 and the Vite server keeps
serving it over the proxy as before.

Three settings decide whether a deployment works, and none of them show up
locally.

**The origin must be registered with the identity provider.** The realm in
`docker/keycloak` lists only the development origins. A deployment's own
`https://host/signin-oidc` has to be added to the client's redirect URIs, and
the origin to its web origins, or sign-in fails at the callback. Sign-out is a
second registration: `https://host/signout-callback-oidc` has to be added to
the client's post-logout redirect URIs, or the provider rejects the end-session
request and the user is left signed in at the provider.

**Forwarded headers decide the redirect URI.** The container listens on plain
HTTP and expects TLS to be terminated ahead of it, so the scheme and host it
builds the OpenID Connect redirect URI from come from `X-Forwarded-Proto` and
`X-Forwarded-Host`. Only loopback is believed by default; name the proxy, or
the network it sits on, before trusting it:

```json
{
  "ForwardedHeaders": {
    "KnownProxies": ["10.1.2.3"],
    "KnownNetworks": ["10.1.0.0/16"]
  }
}
```

**Data protection keys must outlive the container.** The key ring encrypts two
things: session cookies, and every user's stored provider API key. Losing it
signs everyone out, stops two replicas reading each other's cookies, and leaves
saved provider keys unreadable — the API is write-only, so a user cannot even
look up what they had and must enter it again.

The image writes the ring to `/keys`, owned by the user it runs as, so
persisting it is a mount rather than a mount plus a setting:

```sh
docker run --volume sleeky-todo-keys:/keys ... sleeky-todo
```

Set `DataProtection:KeyRingPath` only to move it somewhere else, such as a
location every replica shares:

```json
{
  "DataProtection": { "KeyRingPath": "/keys" }
}
```

Without a volume the keys still live on the container's writable layer and
still vanish with it. Two rules make the mount worth having:

- **Back the ring up with the database, and restore them together.** Restoring
  MongoDB against a different ring gives you a database of provider keys nobody
  can decrypt. They are one backup unit.
- **Never prune the directory.** Keys roll roughly every 90 days and the old
  ones stay behind to read what they encrypted. A key saved eight months ago is
  readable only by the key that protected it.

A bind mount is the exception to the ownership note above: its permissions come
from the host, so the directory has to be writable by the container's user
(`APP_UID`, `1654` on the .NET base images) before it is mounted.
