# Local Regulator Accounts

Local Admin portal login URL: `http://localhost:5001/login`

These are local development regulator portal accounts created on March 13, 2026.

| Regulator | Tenant Name | Tenant Slug | Tenant ID | Username | Email | Role | Password |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CBN | Central Bank of Nigeria | `cbn` | `A5C941E5-2150-4BD6-A21C-4D7FAD2979B5` | `cbnregulator` | `cbnregulator@regos.local` | `Admin` | `Password123!` |
| NDIC | Nigeria Deposit Insurance Corporation | `ndic` | `9A7E6D72-C15F-47CA-A5EC-04036790E28E` | `ndicregulator` | `ndicregulator@regos.local` | `Admin` | `Password123!` |
| NAICOM | National Insurance Commission | `naicom` | `16B0F529-B241-48A1-BC56-42CC1D3800D5` | `naicomregulator` | `naicomregulator@regos.local` | `Admin` | `Password123!` |
| SEC | Securities and Exchange Commission | `sec` | `93AE9385-D06D-4503-AEA1-49DEA72D6353` | `secregulator` | `secregulator@regos.local` | `Admin` | `Password123!` |

## Notes

- These are local/dev credentials only.
- Each account has the required `Registration` and `DataProcessing` consent records for policy version `2.1`.
- Each account was validated against the running Admin app and returned a successful login redirect to `/`.
