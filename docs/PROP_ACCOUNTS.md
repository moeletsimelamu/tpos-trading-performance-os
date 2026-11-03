# Prop firm accounts

How prop firm accounts work as built. The plan and stages are in [`../PLAN.md`](../PLAN.md) §7c, with decisions D10 to D12 in §12.

**Built so far (P8.1):** the account records, their rules and their history. Linking trades, payouts and fees, and calculating against the rules, come in later stages. Until then, nothing on these pages reads a trade or touches the ledger.

## One record per attempt (D10)

A record covers one attempt, from buying the account until it ends.

| What happens | How it is recorded |
|---|---|
| You pass a phase | Same record, new phase, and the balance restarts |
| You buy a reset | Same record, same phase, active again, and the balance restarts |
| You buy a new challenge after a breach | A new record |

**Why:** fees are paid at the start and payouts arrive at the end. On one record, an attempt's fees and payouts sit together, so a failed attempt shows fees with no payout, and a passed one does not.

**What it costs:**
- Firms often issue a new login for each phase, so the login is kept on each history entry as well as on the account.
- Results for a single phase will need a filter by date, because one record's trades span its phases.

Only prop firm accounts get records (D11). Trades on any other account keep the free-text account field.

## What is recorded

- **The account:** firm, nickname, size, currency, phase, status, the login or account number at the firm, notes and the opening date. The app never asks for the account's password, and the forms say not to enter it.
- **Phases:** Evaluation, Verification, Funded. An account can open in any of them, and a phase can be skipped, because firms differ.
- **Statuses:**

  | Status | Meaning | Tag |
  |---|---|---|
  | Active | Live | `tag-positive` |
  | Pending | Waiting on the firm, for example for verification or the next login | `tag-warning` |
  | Breached | A rule was broken | `tag-negative` |
  | Closed | Finished; hidden from the list unless asked for | neutral `tag` |

- **Starting balance:** where the balance last restarted, and the date it did. It is the account size unless another amount is given.

## Rules

Each version of an account's rules holds the following, in the account's currency. Every limit is optional, because empty means the firm sets none.

| Rule | Checked when saved |
|---|---|
| Drawdown model: none, static, trailing end of day, or trailing intraday | A model needs a limit, and no model cannot have one |
| Drawdown limit | More than zero, whole cents, less than the starting balance |
| Whether trailing stops once it reaches the starting balance | Only kept for a trailing model |
| Daily loss limit | More than zero, whole cents, less than the starting balance |
| Profit target | More than zero, whole cents |
| Minimum trading days | 1 to 1,000 |
| Your payout split | More than 0% and at most 100%, two decimal places |
| Stated purchase, reset and monthly fees | More than zero, whole cents |
| Notes, for anything else the firm sets | Up to 500 characters |

- **Limits are amounts, not percentages.** A firm's "10%" is entered as the amount it comes to.
- **Stated fees are for reference.** They are the firm's prices. They are not recorded as paid, and fees actually paid will be expenses in the ledger (P8.3).
- **Rules change by adding a version**, with the date it applies from. Earlier versions are kept and shown. Saving identical rules adds nothing.
- **Dates stay in order.** New rules cannot apply before the current phase began or before the current version's date, and a later phase change or reset cannot be dated before rules already in force.

## History

Every change appends an entry that is never edited or deleted: what happened, its date, the phase, status and login after it, any new starting balance, and a note.

| Change | Rules |
|---|---|
| **Phase change** | Only from Active or Pending. Needs the next phase's rules, which start as a copy of the current ones and become a new version. The login stays unless a new one is given. Going back to an earlier phase is a correction and needs a reason. |
| **Status change** | A breach says which rule. Undoing a breach or reopening a closed account needs a reason. The date cannot be before the last phase, status or reset change. |
| **Reset** | Not on a closed account. The account becomes active in the same phase. |
| **Rule change** | Not on a closed account. See above. |
| **Details corrected** | Not on a closed account. Records what changed. The currency is fixed. Correcting the size also corrects the current starting balance, because a size only changes when it was entered wrongly; a firm's scale-up arrives as a phase change or reset with a new starting balance. |

The history is listed in the order things were recorded, newest first. An entry can therefore show an earlier date than the one below it, when something was recorded after the fact.

**Dates:** none can be later than tomorrow, because dates are the trader's local dates and the server's clock is UTC.

**Nicknames:** a trader's open accounts have different nicknames, ignoring case. A closed account's nickname can be used again, and reopening that account is then refused with a 409 until one of them is renamed.

## Access

Accounts belong to the trader who created them, like strategies and trades. Another user gets a 404. Any signed-in user can use `/api/prop-accounts`; the links to the ledger in P8.3 will need the Owner role, like the rest of finance.

## Limits of this stage

- No trade, payout or expense is linked to an account yet.
- Nothing is calculated against the rules yet: no balance, drawdown room or warning.
- Rule limits are only checked against the starting balance when they are saved. They are not checked against each other, for example a daily loss limit larger than the drawdown limit, because firms' rules vary.
