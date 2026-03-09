# RegOS™ — Test Users

## Admin Portal (http://localhost:5001)

| # | Username | Display Name | Email | Role | Password | Status |
|---|----------|-------------|-------|------|----------|--------|
| 1 | `admin` | System Administrator | admin@fcengine.local | Admin | `Admin@123` | Active |
| 2 | `bsdadmin@cbn.gov.ng` | BSD ADMIN | bsdadmin@cbn.gov.ng | Admin | `Admin@123` | Active |

> The default admin password is configured via `DefaultAdmin__Password` env var (default: `Admin@123`).
> The `bsdadmin` user was created through the portal UI with the same default password.

---

## FI Portal (http://localhost:5300)

**Institution:** Sample Finance Company Ltd (`FC001`)

| # | Username | Display Name | Email | Role | Password | Status |
|---|----------|-------------|-------|------|----------|--------|
| 1 | `admin` | Admin User | admin@fc001.com | Admin | `Admin@123` | Active |
| 2 | `maker1` | John Maker | maker1@fc001.com | Maker | `Admin@123` | Active |
| 3 | `checker1` | Jane Checker | checker1@fc001.com | Checker | `Admin@123` | Active |
| 4 | `viewer1` | Bob Viewer | viewer1@fc001.com | Viewer | `Admin@123` | Active |

> FI Portal users were seeded for institution `FC001`. All use `Admin@123` as the default password.

---

## Institutions

| # | Code | Name | License Type | Status |
|---|------|------|-------------|--------|
| 1 | `FC001` | Sample Finance Company Ltd | Finance Company | Active |
| 2 | `FC002` | Example Microfinance Bank | Microfinance Bank | Active |

> `FC002` has no users yet. Users can be created via the Admin Portal's institution management.

---

## Role Permissions

### Admin Portal Roles
| Role | Permissions |
|------|------------|
| **Admin** | Full access — manage templates, users, rules, approvals, view audit logs |
| **Approver** | Review and approve submissions, manage business rules |
| **Viewer** | Read-only access to all portal data |

### FI Portal Roles
| Role | Permissions |
|------|------------|
| **Admin** | Manage institution team members, view all submissions, institution settings |
| **Maker** | Create and submit returns, upload XML data, view validation results |
| **Checker** | Review, approve or reject submissions created by Makers |
| **Viewer** | Read-only access to institution submissions and data |

---

## API (http://localhost:5002)

Authentication is via API key header:

```
X-Api-Key: <configured via API_KEY env var>
```

> By default, API key auth is disabled when `API_KEY` is empty.

---

## Database

| Setting | Value |
|---------|-------|
| Server | `localhost:1433` |
| Database | `FcEngine` |
| Username | `sa` |
| Password | `YourStrong@Passw0rd` |

---

## Security Notes

- Admin Portal users are locked after **5 failed login attempts** for **15 minutes**
- FI Portal users are locked after **5 failed login attempts** for **15 minutes**
- Passwords are hashed with **PBKDF2-HMAC-SHA256** (100,000 iterations)
- FI Portal users created via Team Members page have `MustChangePassword = true` by default
