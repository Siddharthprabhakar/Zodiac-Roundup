# Zodiac Roundup Cloudflare deployment

This Worker streams the private `zodiac-roundup-webgl` R2 bucket and serves the
Unity WebGL build. The main website remains on Framer; the game will use
`play.wakeupsid.com` after the Cloudflare zone becomes active.

## Commands

Run from this directory:

```powershell
npm install
npx wrangler login
.\upload-webgl.ps1
npx wrangler deploy
```

The Worker is first tested on its generated `workers.dev` URL. Add the custom
domain only after the Cloudflare zone for `wakeupsid.com` is active.
