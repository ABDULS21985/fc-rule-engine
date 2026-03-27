# Demo Credentials

Generated: 2026-03-27 00:49:48 UTC

## Shared Password

All seeded demo accounts use `Admin@FcEngine2026!`.

## URLs

- Admin portal: `http://localhost:5200/login`
- Institution portal: `http://localhost:5300/login`

## Platform Accounts

| Username | Role | Email | Password | MFA |
| --- | --- | --- | --- | --- |
| `admin` | `Admin` | `admin@fcengine.local` | `Admin@FcEngine2026!` | Not required |
| `platformapprover` | `Approver` | `platform.approver@fcengine.local` | `Admin@FcEngine2026!` | Not required |
| `platformviewer` | `Viewer` | `platform.viewer@fcengine.local` | `Admin@FcEngine2026!` | Not required |

## Central Bank of Nigeria (CBN)

- Audience: `Regulator`
- Login URL: `http://localhost:5200/login`
- Notes: Regulator workspace demo with policy simulation, stress testing, surveillance, and examination workflows.

| Username | Role | Email | Password | MFA |
| --- | --- | --- | --- | --- |
| `cbnadmin` | `Admin` | `cbn.admin@fcengine.local` | `Admin@FcEngine2026!` | Not required |
| `cbnapprover` | `Approver` | `cbn.approver@fcengine.local` | `Admin@FcEngine2026!` | Required |
| `cbnviewer` | `Viewer` | `cbn.viewer@fcengine.local` | `Admin@FcEngine2026!` | Not required |

### MFA: CBN Demo Approver (cbnapprover)

- TOTP secret: `NS3KP7FGNAQZDUBVMYOG4UXKS7PFKMEQ`
- Backup codes: `6E58-4E5F`, `3575-E269`, `69B6-4EBC`, `2C91-3E46`, `CC03-ECCE`, `9837-6C29`, `21C0-1283`, `39B2-B013`, `3833-FE25`, `BB45-3634`

## Access Bank Plc (ACCESSBA)

- Licence type: `DMB`
- Login URL: `http://localhost:5300/login`
- Notes: DMB demo tenant with seeded DMB_BASEL3 history and zero-warning DMB_OPR sample.

| Username | Role | Email | Password | MFA |
| --- | --- | --- | --- | --- |
| `accessdemo` | `Admin` | `accessdemo@accessbank.local` | `Admin@FcEngine2026!` | Not required |
| `accessapprover` | `Approver` | `approver@accessbank.local` | `Admin@FcEngine2026!` | Required |
| `accesschecker` | `Checker` | `checker@accessbank.local` | `Admin@FcEngine2026!` | Required |
| `accessmaker` | `Maker` | `maker@accessbank.local` | `Admin@FcEngine2026!` | Not required |
| `accessviewer` | `Viewer` | `viewer@accessbank.local` | `Admin@FcEngine2026!` | Not required |

### MFA: Access Demo Checker (accesschecker)

- TOTP secret: `PLI7J3HZ2IV4I7N7A6ZNTJQLSCU6QDPG`
- Backup codes: `FCDE-7BCC`, `4C43-5492`, `EA40-BEDD`, `4976-C383`, `D09A-164C`, `641B-73C0`, `A728-4165`, `ECA9-3901`, `7141-6257`, `2F4B-501A`

### MFA: Access Demo Approver (accessapprover)

- TOTP secret: `KY6WOKCEBRLLXAUF6FAW7VRX3UZDSS5A`
- Backup codes: `9464-CA37`, `1C10-BD25`, `3025-D417`, `2F06-C5D9`, `A6DC-C7DE`, `852E-1A5E`, `A79D-B915`, `B3C2-CF68`, `3E67-539C`, `3B3C-4842`

## CASHCODE BDC LTD (CASHCODE)

- Licence type: `BDC`
- Login URL: `http://localhost:5300/login`
- Notes: BDC demo tenant with live BDC_CBN templates and samples.

| Username | Role | Email | Password | MFA |
| --- | --- | --- | --- | --- |
| `cashcodeadmin` | `Admin` | `admin@cashcode.local` | `Admin@FcEngine2026!` | Not required |
| `cashcodeapprover` | `Approver` | `approver@cashcode.local` | `Admin@FcEngine2026!` | Required |
| `cashcodechecker` | `Checker` | `checker@cashcode.local` | `Admin@FcEngine2026!` | Required |
| `cashcodemaker` | `Maker` | `maker@cashcode.local` | `Admin@FcEngine2026!` | Not required |
| `cashcodeviewer` | `Viewer` | `viewer@cashcode.local` | `Admin@FcEngine2026!` | Not required |

### MFA: Cashcode Checker (cashcodechecker)

- TOTP secret: `5TJHISPG66MXZGLT734S3ARQY4FV6T7Z`
- Backup codes: `0800-EC3C`, `62B6-F892`, `F2C8-0065`, `8F87-DDBF`, `2550-8FB7`, `3E74-4F9F`, `2278-B389`, `AEFA-459D`, `421C-B2BF`, `86DA-D34E`

### MFA: Cashcode Approver (cashcodeapprover)

- TOTP secret: `4CVGOKPPJ5IP46ELCKH5GUOQTJGQ6LGW`
- Backup codes: `30B1-EF80`, `BF03-89C0`, `9DCC-337D`, `9F11-A39F`, `27C2-5060`, `08C7-2165`, `F466-D78F`, `AB31-49D4`, `B1C2-E493`, `90A7-23C0`

## Notes

- Backup codes are one-time use. If you exhaust them, rerun `seed-demo-credentials` to rotate MFA material.
- The institution `Checker` and `Approver` roles require MFA by design, so this pack includes both a TOTP secret and backup codes for those users.
- The admin portal still treats tenantless users as platform-level sessions. `Admin`, `Approver`, and `Viewer` accounts are seeded and usable, but platform least-privilege separation is not strict yet.
