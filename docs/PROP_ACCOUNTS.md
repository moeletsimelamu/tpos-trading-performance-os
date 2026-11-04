# Prop firm accounts

How prop firm accounts work as built. The plan and stages are in [`../PLAN.md`](../PLAN.md) §7c, with decisions D10 to D12 and D16 in §12.

**Built so far:** the account records, their rules and their history (P8.1); linked trades, balances from the firm's dashboard, each account's standing against its rules, and warnings (P8.2); fees and payouts linked to an account, and the report on which accounts cost money without paying back (P8.3). Nothing on these pages posts to the ledger.

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
- **A new version of the rules applies to the whole run.** The standing is measured with the current version, from the start of the current phase or reset.
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

## Trades on an account

A trade can be linked to an account on the trade form, or a whole CSV file can be put on one when importing, because a CSV has no account column.

- **Name:** a linked trade shows the account's nickname, and follows it if the account is renamed. The trade log and every analytics filter use that name, so results for one account work as they do for free-text names.
- **Currency:** a linked trade takes the account's currency. One stated in another currency is refused.
- **Other accounts:** trades on any other account keep the free-text field (D11). Linking clears it.
- **Whose accounts:** only your own. A closed account takes no new trades, though one already on it can still be corrected. A breached account can still take them, because catching up on trades already taken is normal.
- **History:** moving a trade on or off an account is in the trade's change history.
- **Import matches:** "Import as a separate trade" on a row waiting for a decision does not link it. Edit the trade to link it.

## Balances from the firm's dashboard

You can enter the balance the firm shows, as at the end of a day, with the drawdown level it shows if you want. There is one per day, within the current run, and a wrong one can be removed; the history keeps that it was there. A balance never moves money and never counts as cash.

**After a payout, enter the drawdown level too.** The balance alone cannot tell a withdrawal from a loss, so without the level the app would see the drop as lost room.

Each balance in the current run is listed beside what the recorded trades came to on that date. A difference means trades are missing or wrong, or money left the account.

## Where an account stands (D12)

Worked out on each read from the account's closed and reviewed trades and its balances, and never stored. **These are the app's figures, not the firm's.** The firm's own numbers decide, trades not recorded are not in them, and unrealised profit and loss cannot be seen from closed trades. The pages say so.

| Figure | How it is worked out |
|---|---|
| **Trades counted** | Closed or reviewed, closed on or after the start of the current run. The closing date is the exit date, or the trade date without one. Open trades, and trades closed before the run, are counted apart and named on the page |
| **Balance** | Starting balance + the P&L of counted trades, taken day by day. A balance entered replaces it as of the end of that day, and later trades move on from there. P&L is net as the platform reports it, so fees are not taken off again |
| **Profit or loss** | Balance − starting balance |
| **Drawdown level (static)** | Starting balance − limit |
| **Drawdown level (trailing, end of day)** | Rises with each day's closing balance: the higher of the current level and closing balance − limit. It never falls |
| **Drawdown level (trailing, intraday)** | The same, after each closed trade, in the order they closed. The firm's level can be higher, because it follows unrealised profit too; the page says so |
| **Lock** | With "stops trailing once it reaches the starting balance", the level does not rise above the starting balance |
| **A level you entered** | Replaces the calculated level as of that day. Later highs can move it up as above |
| **Drawdown room** | Balance − level. **Near** at 20% of the limit or less, **at the limit** at zero or below |
| **Daily loss** | The net P&L of each closing day. A day that lost the limit or more is listed and stays **at the limit** for the rest of the run, even after recovering. **Near** at 80% of the limit, for the current day only |
| **Target** | Profit against the target, and what remains. It can go down again |
| **Trading days** | Distinct closing days with counted trades. A day with only a balance entered is not one |
| **Target met** | The target is reached, and the minimum days are too if there are any |

## Warnings

The Executive overview warns about **active** accounts only:

| When | Tone |
|---|---|
| At the drawdown limit, or a day past the daily loss limit | Negative |
| Near the drawdown limit, or near the daily loss limit today | Warning |
| Target met | Information |

A warning never blocks a trade and never changes an account. Recording a breach or a reset is yours to do, which also clears the warning.

## Fees and payouts (D16)

An account's page has a **Fees and payouts** section, and the list page has **Fees against payouts**, which answers which accounts cost money without paying anything back.

- **Fees** are expenses already in the ledger. Record the expense in Transactions first, then link it and say what it paid for: buying the account, a reset, a monthly fee or something else. The app never records the expense for you, so a fee in the rules is not treated as paid until its expense is linked.
- **Payouts** are linked when requesting one, or later from the account's page. A rejected payout cannot be linked, and one linked to another account must be unlinked first.
- **Linking moves no money.** The expense or payout already exists, no balance changes, and removing a link leaves it in place.

| Outcome | Means |
|---|---|
| Fees, no payout received | At least one fee counted, and no payout received |
| Payouts received | At least one payout received, whatever the fees came to |
| Nothing linked | No fee counted and no payout received |

What counts:
- A reversed fee is shown but not counted.
- A payout counts once received, for the amount that reached the bank account, in that account's currency. Undoing the receipt takes it back out.
- A requested or approved payout is shown as awaiting and is never in the figures.
- Each currency is its own line and nothing is converted, so a USD fee and a payout credited in ZAR are two lines, and neither net shows the whole picture for that account.
- The report includes closed accounts, and says how many received payouts are not linked to any account and so not counted.

## Access

Accounts belong to the trader who created them, like strategies and trades. Another user gets a 404. Any signed-in user can use `/api/prop-accounts` and link their own trades. Fees and payouts need the Owner role, like the rest of finance, and a user without it does not see those sections. Even an owner reaches only their own accounts.

## Limits of this stage

- A fee is not compared with the fee in the account's rules, and a monthly fee that was never linked does not show as missing.
- **Balances after a payout:** the app does not take a payout off the balance by itself. Enter the balance and level from the dashboard.
- **Other firm rules:** consistency rules, news restrictions and time limits are notes only, and nothing is measured against them.
- **Daily loss** is measured from closed trades per closing day. Firms that measure from the day's starting equity, or at their own server's day boundary, can differ.
- **"Today"** is the server's UTC date, a known gap across the app, so "near" for the current day allows a trade dated a day ahead.
- **Results by phase:** Performance filters by account, not by phase, so a phase's results need a date filter.
- Rule limits are only checked against the starting balance when they are saved. They are not checked against each other, for example a daily loss limit larger than the drawdown limit, because firms' rules vary.
