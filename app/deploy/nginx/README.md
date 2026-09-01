# Deploy the LoncherApp portal behind nginx on the VPS

Public IP of this VPS: **69.197.174.177** · Domain: **loncherapp.com**

> All steps below need **root / sudo**. The `schoolPOS` user is unprivileged, so
> run these after logging in as `root` (or add it to sudo: `usermod -aG sudo schoolPOS`).

Files in this folder:
- `loncherapp.com.conf` — the nginx site (HTTP→HTTPS redirect + reverse proxy to Kestrel).
- `../systemd/schoolpos-portal.service` — runs the app as a service on `127.0.0.1:5170`.

---

## 0. DNS (do this first, at your registrar — not on the server)
```
A   @     69.197.174.177
A   www   69.197.174.177
```
Verify: `dig +short loncherapp.com` → `69.197.174.177`. Nginx TLS issuance needs this
resolving first.

## 1. Publish the app
```bash
# on a machine with the .NET 8 SDK, from the repo:
dotnet publish app/src/SchoolPOS.Portal.Web -c Release -o ./publish
# copy ./publish to the VPS:
sudo mkdir -p /var/www/loncherapp
sudo rsync -a ./publish/ /var/www/loncherapp/
sudo chown -R www-data:www-data /var/www/loncherapp
```
Production config (two files — templates in `../config-templates/`):
```bash
# 1) non-secret settings next to the DLL
sudo cp app/deploy/config-templates/appsettings.Production.template.json \
        /var/www/loncherapp/appsettings.Production.json
sudo nano /var/www/loncherapp/appsettings.Production.json   # SchoolId, App ID, issuer RFC, etc.

# 2) secrets in a root-only env-file (loaded by the systemd unit, overrides the JSON)
sudo mkdir -p /etc/schoolpos
sudo cp app/deploy/config-templates/loncherapp.env.template /etc/schoolpos/loncherapp.env
sudo chown root:root /etc/schoolpos/loncherapp.env
sudo chmod 600       /etc/schoolpos/loncherapp.env
sudo nano /etc/schoolpos/loncherapp.env    # DB password, MP tokens, SMTP + SW passwords
```
The filled-in `appsettings.Production.json` is gitignored; the `loncherapp.env` lives
outside the repo. **Never commit either.** Nested keys in the env-file use `__`
(e.g. `Smtp__Password`, `ConnectionStrings__Portal`).

> **Never create a `secrets.json` file inside `src/SchoolPOS.Portal.Web/`** for local dev
> credentials — use `dotnet user-secrets set Key Value --project app/src/SchoolPOS.Portal.Web`
> instead (it's stored outside the repo/project tree entirely). A loose `secrets.json` there
> used to get silently swept into `dotnet publish`'s output by the SDK's default file
> globbing and load *after* `appsettings.Production.json`, quietly overriding it — this is
> exactly how production once ended up pointed at a dev database. The project now excludes
> `secrets.json` from publish (`<Content Remove>`) as a second line of defense, but don't
> rely on that: just don't create the file there at all.

## 2. Install the .NET 8 runtime + run the service
```bash
sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-8.0   # or the Microsoft package feed
sudo cp app/deploy/systemd/schoolpos-portal.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now schoolpos-portal
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:5170/   # expect 200
```

## 3. Install nginx + the site config
```bash
sudo apt-get install -y nginx
sudo mkdir -p /var/www/certbot
sudo cp app/deploy/nginx/loncherapp.com.conf /etc/nginx/sites-available/
sudo ln -s /etc/nginx/sites-available/loncherapp.com.conf /etc/nginx/sites-enabled/
sudo rm -f /etc/nginx/sites-enabled/default
```

## 4. Firewall
```bash
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow OpenSSH        # don't lock yourself out
sudo ufw enable
```

## 5. TLS certificate (Let's Encrypt)
The config references `/etc/letsencrypt/live/loncherapp.com/…`, which don't exist yet.
Easiest is certbot's nginx plugin (it edits + reloads nginx for you):
```bash
sudo apt-get install -y certbot python3-certbot-nginx
sudo certbot --nginx -d loncherapp.com -d www.loncherapp.com \
     --redirect --agree-tos -m admin@loncherapp.com --no-eff-email
```
Auto-renewal is installed by the certbot package (`systemctl list-timers | grep certbot`).

> If you prefer to keep the shipped config verbatim, use the **webroot** method instead:
> `sudo certbot certonly --webroot -w /var/www/certbot -d loncherapp.com -d www.loncherapp.com`,
> then `sudo nginx -t && sudo systemctl reload nginx`.

## 6. Test & reload
```bash
sudo nginx -t                       # config syntax OK?
sudo systemctl reload nginx
curl -I https://loncherapp.com/      # expect HTTP/2 200
```

## 7. Point the integrations at the domain
- **Mercado Pago** webhook URL → `https://loncherapp.com/api/payments/webhook`
- **Mercado Pago OAuth** redirect → `https://loncherapp.com/oauth/mercadopago/callback`
  (both require HTTPS — now satisfied).

---

### Notes
- The app binds `127.0.0.1:5170` (loopback only); nginx is the public front door on 80/443.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (in the unit) makes the app honor nginx's
  `X-Forwarded-Proto`, so generated links and cookie `Secure` flags are correct.
- To change the app port, update it in **both** the systemd unit (`ASPNETCORE_URLS`) and
  `upstream schoolpos_portal` in the nginx conf.
