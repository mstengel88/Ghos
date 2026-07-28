# Dispatch V2 to GHOS integration

Dispatch V2 is the source of truth. GHOS reads its versioned export endpoint
using a dedicated shared secret.

## Canonical configuration

The same random value must be configured in two places:

Dispatch V2 `.env`:

```env
GHOS_INTEGRATION_SECRET=REPLACE_WITH_A_LONG_RANDOM_VALUE
```

GHOS `/opt/ghos/.env`:

```env
DISPATCH_BASE_URL=https://dispatch.winterwatch-pro.info
GHOS_DISPATCH_INTEGRATION_SECRET=REPLACE_WITH_THE_EXACT_SAME_VALUE
```

The GHOS environment value takes precedence over the older encrypted value in
the GHOS Settings database. This prevents an obsolete saved copy from silently
winning after a key rotation.

After changing either environment file, recreate the affected container.
Restarting only the browser or rebuilding an image does not guarantee that a
running container receives new environment values.

Dispatch V2:

```bash
cd /path/to/dispatch-v2-sandbox
docker compose up -d --build --force-recreate dispatch-v2-sandbox
```

GHOS:

```bash
cd /opt/ghos
docker compose up -d --build --force-recreate ghos-web
```

## Safe diagnosis

GHOS displays the first 12 hexadecimal characters of the SHA-256 fingerprint
for the server-managed secret. It never displays the secret itself.

On the Dispatch V2 host, calculate the same fingerprint from the effective
Compose configuration without printing the secret:

```bash
docker compose config --format json |
  jq -r '.services["dispatch-v2-sandbox"].environment.GHOS_INTEGRATION_SECRET // .services["dispatch-v2-sandbox"].environment.DISPATCH_IMPORT_SECRET // empty' |
  tr -d '\n' |
  sha256sum |
  cut -c1-12
```

The fingerprint must match GHOS Settings. If it does not, update the GHOS
environment and force-recreate `ghos-web`.

The Dispatch endpoint returns:

- `200`: the secret matches and the export is available;
- `401`: the running container has a different secret;
- `503`: neither `GHOS_INTEGRATION_SECRET` nor its temporary
  `DISPATCH_IMPORT_SECRET` fallback is configured;
- `404`: the GHOS export route is not deployed.

## Rotation

1. Generate a new high-entropy secret and store it in the password manager.
2. Update Dispatch V2 and force-recreate its container.
3. Update GHOS and force-recreate `ghos-web`.
4. Confirm the fingerprints match.
5. Run **Sync dispatch now** and confirm a successful import.
6. Remove the retired value from operational notes and clipboard history.
