# GHOS remote command access

GHOS is reachable through Tailscale at `100.75.152.30`. The dedicated Codex
key uses a nonstandard filename, so a plain `ssh ghosadmin@100.75.152.30`
command does not automatically select it.

Use the checked-in helper:

```bash
./tools/ghos-ssh.sh
```

Run a noninteractive command:

```bash
./tools/ghos-ssh.sh 'cd /opt/ghos && docker compose ps'
```

The helper always uses:

- user: `ghosadmin`;
- Tailscale address: `100.75.152.30`;
- key: `~/.ssh/ghos_codex_ed25519`;
- batch and identities-only authentication.

Override these without editing the script:

```bash
GHOS_SSH_HOST=192.168.36.207 ./tools/ghos-ssh.sh
```

The matching public-key fingerprint is:

```text
SHA256:HfrcIvHYZnsQz5GnXJsjsntO4TKqJ+yppwLUkvzqHx8
```

Never commit or transmit the private key. If access stops working, verify that
the public key remains in `/home/ghosadmin/.ssh/authorized_keys` and that its
permissions are `0700` for `.ssh` and `0600` for `authorized_keys`.
