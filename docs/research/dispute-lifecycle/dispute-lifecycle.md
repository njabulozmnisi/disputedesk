---
title: 'Domain research: card dispute lifecycle norms (South Africa)'
type: 'domain'
topic: 'dispute-lifecycle'
decision: 'Calibrate a demo South African retail-bank card dispute product to real-world norms'
source: 'native run (quick preset, two dimensions)'
status: complete
preset: 'quick'
validation: 'normal'
claims_verified: 7
claims_unverified: 6
created: '2026-10-25'
updated: '2026-10-25'
---

# Domain research: card dispute lifecycle norms (South Africa)

**Decision this research serves:** calibrate a demo dispute product for a South African retail bank, where customers dispute card transactions, to real-world norms. The product decisions already taken are checked here, not reopened.

**Scope:** two dimensions only: (1) rules and norms, (2) vocabulary and glossary. Market, value chain, key players and competitors are out of scope.

## Executive summary

The decisions already taken hold up. One needs a wording change, one has a logical gap, and none is contradicted outright.

- **The 120-day window matches the card schemes.** Visa's issuer time limit is 120 calendar days from the **transaction processing date** [2][3]. Mastercard's is 120 days from the Central Site Business Date [4]. Both dates are scheme processing dates, close to settlement. Neither is the date the transaction posts to the account. Exceptions run longer: Visa not-received disputes count from the expected delivery date, up to 540 days [3]. Some limits are shorter: Mastercard authorisation disputes get 90 days [4]. A fixed 120 days from the transaction date is a fair simplification for the demo. It is worth stating as one.
- **No South African rule fixes a number of days.** The Conduct Standard for Banks requires acknowledgement "within a reasonable time" [8]. The National Financial Ombud Scheme (NFO), which replaced the Ombudsman for Banking Services on 1 March 2024 [5], first requires the bank's internal process to be exhausted, with no day count [5]. When the NFO refers a complaint back, it gives the bank **21 working days** to respond in writing [6]. A 10-calendar-day internal escalation trigger sits well inside these limits and is defensible. One bank quotes a full chargeback taking 21 business days locally and 45 days internationally (low confidence) [10]. So 10 days works as an escalation trigger, not as a resolution target.
- **The biggest caveat is the primary scheme rules.** The Visa Core Rules and the Mastercard Chargeback Guide could not be retrieved (403 and not public). The 120-day and 540-day figures come from consistent secondary sources and the 2026 Visa merchant guide's own 120-day references. They have not been read in the rule text itself.

## 1. Rules and norms

### Card scheme time limits

Visa's 2026 merchant guide defers its time-limit table to the Visa Rules [1]. Secondary sources agree that "a maximum of 120 calendar days may pass between the transaction processing date and the dispute processing date" [2]. For not-received cases the window runs from the expected receipt date, "not to exceed 540 calendar days from the transaction processing date" (medium confidence; a forum quotes the rule footnote) [3]. Mastercard uses 120 days from the Central Site Business Date, with a 540-day extension for narrow service cases and 90 days for authorisation-related disputes (medium-low confidence, secondary sources only) [4].

The scheme limit is the issuer's deadline to raise the chargeback, not the customer's. A bank that lets customers file on day 120 has no time left to raise the chargeback itself. This is an inference, not a sourced rule. It explains why a South African bank has historically used a shorter customer window, but that report is old and was not retrieved (see Open questions).

### South African complaint and resolution timeframes

- **Ombud:** the NFO took over from the Ombudsman for Banking Services on 1 March 2024, and the old OBS URL redirects to the NFO [5]. A complaint is "premature" until the bank's internal process is exhausted (Rule 7.3.1), and the rules set no fixed number of days for that [5]. The NFO may decline a complaint brought three or more years after the customer became aware of the problem [5]. It acknowledges a complaint within one week, and its banking FAQ sets no waiting period [7]. It asks the bank to respond in writing within 21 working days when the complaint was not first lodged with the bank [6].
- **Conduct Standard for Banks (3 of 2020), section 8:** the bank acknowledges a complaint "within a reasonable time", explains the process, keeps the customer informed of progress and delays, and gives the ombud's details [8]. In 2025 an FSCA review found that most banks fell short of section 8 [9].
- **Bank practice:** one bank's community forum quotes 21 business days for local chargebacks and 45 days for international ones (low confidence; known only from a search summary because the page would not load) [10].
- **Statute:** an academic paper argues that neither the Consumer Protection Act nor the National Credit Act compels a bank to raise a chargeback [20]. Chargebacks are a card scheme right, not a statutory one (low confidence; older work).

**Effect on the 10-calendar-day SLA:** nothing contradicts it. As an internal "escalate if unresolved" trigger it is stricter than any external limit found. As a promise to resolve within 10 days it would be unrealistic whenever the merchant must respond, because scheme response windows alone run weeks [10].

### Provisional credit (excluded from the product; norm recorded)

US Regulation E requires a decision within 10 business days. The investigation may extend to 45 days only if the bank provisionally credits the account within those 10 business days [11]. US banks show this to the customer, for example "Claim Under Review, Provisional Credit Issued" [12]. **No South African source on provisional credit was found.** The NFO, the Conduct Standard and the bank dispute form say nothing about it [5][8][16]. That absence is the finding: provisional credit is a US regulatory norm, not a documented South African one. Excluding it fits.

### POPIA and record-keeping

- POPIA s14: records are kept no longer than the purpose needs, unless a law, a contract or a lawful function requires them. Afterwards they are destroyed, deleted or de-identified "as soon as reasonably practicable" [17].
- POPIA s19: the bank applies "appropriate, reasonable technical and organisational measures" against loss and unlawful access [18].
- FIC Act s23: transaction records are kept for at least five years after the transaction concludes (medium confidence; statute text through a secondary site) [19]. This is the "required by law" ground that lets dispute records outlive the dispute under s14.
- In practice, a South African bank's dispute form asks for only the "first 7 and last 4 digits" of the card number [16]. Data minimisation of the card number is the norm in practice.

## 2. Vocabulary and glossary

### Lifecycle statuses customers see

Real trackers use few, coarse statuses. One shows "Open" and "Closed" [13]. Another shows active claims moving to closed [14]. A third uses "Claim Under Review" and a provisional-credit variant [12]. The scheme process behind them has stages: the dispute, then possible further review stages such as pre-arbitration and arbitration [1]. Customers are not shown those stages. No bank in the sample shows "Escalated" to the customer [12][13][14]. Escalation is an internal state, so showing it is a product choice, not the industry norm. No source on a customer "Withdrawn" status was found.

### Settled versus pending

US banks accept disputes only on posted transactions, because pending charges can change or drop off [15]. The South African form treats a reserved but declined amount as its own reason, and warns that reversing an authorisation "will not prevent the merchant from processing the transaction" [16]. Both support "settled card debits only".

### Reason categories

| Product category | Visa condition [1] | Seen on a South African bank form [16] |
|---|---|---|
| Unrecognised | 10.4 Other Fraud (card absent), or fraud generally | Partly ("did not authorise the additional transaction") |
| Duplicate charge | 12.6 Duplicate Processing / Paid by Other Means | Yes, as one transaction only / paid another way |
| Wrong amount | 12.5 Incorrect Amount | No |
| Not received | 13.1 Merchandise/Services Not Received | Yes, with expected delivery date |
| Cancelled subscription | 13.2 Cancelled Recurring Transaction | Partly (cancelled bookings) |

The five chosen categories map cleanly onto Visa conditions. They are a representative core. Common categories they leave out: **refund or credit not processed** (13.6; on the form as a credit voucher not showing), **ATM cash not dispensed or partly dispensed** (13.9; first on the form), **not as described or defective** (13.3), and **declined but debited** (form only) [1][16]. In South African practice "unrecognised" often goes through a separate fraud route, because the bank's dispute form has no general fraud option [16].

### Glossary (terms used in sources)

- **Dispute / chargeback:** the customer's challenge is the dispute. The chargeback is the issuer's scheme-level reversal against the acquirer [1].
- **Issuer / acquirer:** the customer's bank / the merchant's bank. Visa runs the framework, and disputes flow through issuers and acquirers [1].
- **Dispute category / condition (reason code):** Visa groups disputes into 10 Fraud, 11 Authorization, 12 Processing Errors and 13 Consumer Disputes. Each has numbered conditions [1]. Mastercard uses reason codes [4].
- **Transaction processing date / Central Site Business Date:** the scheme dates that start the 120-day clock [2][4].
- **Pre-arbitration / arbitration:** the later scheme review stages after the initial dispute [1].
- **Compelling Evidence 3.0:** Visa's framework for merchants to fight 10.4 fraud disputes using earlier undisputed transactions processed 120 to 365 days before [1].
- **Provisional credit:** a temporary credit while the dispute is investigated. It is a US Regulation E norm [11].
- **Participant / premature complaint:** NFO terms for the financial institution, and for a complaint not yet through the bank's internal process [5].
- **Dispute fee:** charged by the bank when it proves the customer authorised the transaction [16].

## Cross-dimension insights

- **The scheme deadline belongs to the bank, not the customer.** The 120-day limit is the issuer's deadline [2][4], and the bank needs some of that time to act. A customer-facing window equal to the scheme limit leaves no margin. Real banks set customer windows shorter than the scheme limit. That last point is an inference supported only by an old report (see Open questions).
- **"Wrong amount" and "full amount only" pull against each other.** For an incorrect amount, what is wrong is the excess over the agreed price [1]. Visa states for authorisation disputes that the dispute amount "may be limited to the portion" not properly authorised [1]. No source confirmed whether 12.5 disputes are capped at the difference. Treat this as an open tension, not a contradiction.

## Recommendations

1. **PRD constraint:** keep 120 days, and define it as "120 calendar days from the transaction date (proxy for the scheme processing date)". Record the 540-day not-received extension in `docs/production-path.md`. Medium confidence: secondary sources agree, but the primary rule text was not read.
2. **PRD constraint:** keep the 10-calendar-day SLA, and call it an *escalation trigger*, not a resolution promise. Cite NFO 21 working days [6] and Conduct Standard "reasonable time" [8] as the outer framing. High confidence for the NFO figure.
3. **Glossary input:** refer to "the National Financial Ombud Scheme (NFO)", not "the Ombudsman for Banking Services" [5]. High confidence.
4. **UX input:** "Escalated" as a customer-facing status is a deliberate product choice, not industry practice [12][13][14]. Keep it if it serves the demo, knowing real trackers show a coarser view. Medium confidence.
5. **Architecture input (POPIA):** store the masked card number only (first 6–7 and last 4 digits) [16]. Treat dispute records as transaction records with a retention period, five years under the FIC Act [19], then de-identify per s14 [17]. Medium confidence on the FIC figure.
6. **Optional:** to look less narrow, add "refund not received" and "ATM cash not dispensed" as categories [1][16]. Low cost, high confidence that they are common.

## Open questions

- The exact Visa Core Rules and Mastercard Chargeback Guide wording on time limits, and whether incorrect-amount chargebacks are capped at the difference. Needs scheme rule access, or a bank's or processor's published extract.
- South African customer-facing dispute windows by bank (an older press report gave 30 and 180 days for two banks; not retrieved, stale). Needs current bank terms and conditions.
- Whether any South African bank gives provisional credit on card disputes. Needs bank terms and conditions or the NFO case digest.
- The section 8 text of the Conduct Standard itself, read through a secondary notice only (FSCA and namiblii both returned 403).

## Source appendix

| [n] | Supports | Publisher | Pub date | Accessed | Confidence |
|---|---|---|---|---|---|
| [1] | Visa categories and conditions, stages, CE3.0, "portion" wording | [Visa, Dispute Management Guide for Merchants](https://usa.visa.com/dam/VCOM/global/support-legal/documents/merchants-dispute-management-guidelines.pdf) | 2026-10 | 2026-10-25 | high |
| [2] | Visa 120 days from transaction processing date | [Chargebacks911, Visa 12.5](https://chargebacks911.com/chargeback-reason-codes/visa/12-5-incorrect-amount/) | n.d. | 2026-10-25 | medium |
| [3] | Visa 540-day not-received cap | [BankersOnline forum (quotes Visa Rules)](https://bankersonline.com/forum/ubbthreads.php/topics/2305149/non-reg-e-dispute-time-limit) | n.d. | 2026-10-25 | medium |
| [4] | Mastercard 120 days from CSBD, 540, 90 | [Chargebacks911, Mastercard time limit](https://chargebacks911.com/mastercard-chargeback-time-limit/) | n.d. | 2026-10-25 | low (search summary) |
| [5] | NFO replaced OBS 1 Mar 2024; premature complaint; 3-year limit | [NFO Scheme Rules](https://nfosa.co.za/wp-content/uploads/2024/02/24-02-28_NFO-Scheme-Rules.pdf) | 2024-02 | 2026-10-25 | high |
| [6] | Bank response within 21 working days | [NFO, How to complain](https://nfosa.co.za/how-to-complain/) | n.d. | 2026-10-25 | high |
| [7] | NFO acknowledges within a week; no banking waiting period | [NFO, FAQs](https://nfosa.co.za/faqs/) | n.d. | 2026-10-25 | high |
| [8] | Conduct Standard s8 "reasonable time" acknowledgement | [JPMorgan SA, Complaints Handling Notice](https://JPMorgan.com/content/dam/jpm/global/disclosures/ZA/summary_policy_on_complaints_management.pdf) | 2023-03 | 2026-10-25 | medium |
| [9] | FSCA review: banks fall short of s8 | [IOL Business Report](https://iol.co.za/business-report/2025-03-18-fscas-review-reveals-critical-gaps-in-banks-complaints-handling-processes/) | 2025-03 | 2026-10-25 | medium |
| [10] | 21 business days local / 45 days international | [Standard Bank Community](https://community.standardbank.co.za/t5/Credit-card/How-long-does-it-take-to-get-a-cancelled-transaction-amount-done/m-p/473787) | 2024-03 | 2026-10-25 | low (unverified) |
| [11] | Reg E 10 business days, 45 days with provisional credit | [CFPB, 12 CFR 1005.11](https://www.consumerfinance.gov/rules-policy/regulations/1005/11/) | n.d. | 2026-10-25 | high |
| [12] | "Claim Under Review, Provisional Credit Issued" | [Regions, claim status terms](https://www.regions.com/help/online-banking-help/fraud/meaning-of-claim-status-terms) | n.d. | 2026-10-25 | medium |
| [13] | Open / Closed dispute tracker | [Chase, report a transaction](https://www.chase.com/digital/customer-service/helpful-tips/credit-cards/desktop/report-transaction) | n.d. | 2026-10-25 | medium |
| [14] | Active to closed claim status | [Bank of America, how to dispute a charge](https://bankofamerica.com/help/how-to-dispute-a-charge/) | n.d. | 2026-10-25 | medium |
| [15] | Disputes only on posted transactions | [Regions, dispute a transaction](https://regions.com/help/dispute-a-transaction) | n.d. | 2026-10-25 | medium |
| [16] | South African bank dispute reasons, masked card number, dispute fee | [FNB, Cheque & Debit Card Dispute Form](https://www.fnb.co.ls/downloads/fraud/FNB_Dispute_and_Outstanding_Authorisation_Reversal_Request.pdf) | n.d. | 2026-10-25 | high |
| [17] | POPIA s14 retention and de-identification | [popia.co.za, s14](https://popia.co.za/section-14-retention-and-restriction-of-records/) | 2021-07 (in force) | 2026-10-25 | high |
| [18] | POPIA s19 safeguards | [popia.co.za, s19](https://popia.co.za/section-19-security-measures-on-integrity-and-confidentiality-of-personal-information/) | 2021-07 (in force) | 2026-10-25 | high |
| [19] | FIC Act s23 five-year records | [acts.co.za, FIC Act s23](https://shop.acts.co.za/financial-intelligence-centre-act-2001/23__period_for_which_records_must_be_kept.php) | 2017 (as amended) | 2026-10-25 | medium |
| [20] | CPA/NCA do not compel chargebacks | [University of Pretoria repository](https://repository.up.ac.za/handle/2263/60046) | n.d. (older) | 2026-10-25 | low |

## Staleness map

Computed with `recon_kit.py staleness` (windows: regulatory 12 months, scheme rules 12 months, practice 18 months; undated pages counted from 2025-01).

| Claim | Class | Re-check by |
|---|---|---|
| Visa 120 days from processing date [2] | scheme-rule | overdue (undated source): re-check against Visa Core Rules |
| Mastercard 120 days from CSBD [4] | scheme-rule | overdue (undated source) |
| Visa categories and conditions [1] | scheme-rule | 2027-10 |
| NFO 21 working days [6] | regulatory | 2027-01 |
| NFO rules: premature complaint, 3 years [5] | regulatory | overdue by window; page live on 2026-10-25 |
| Conduct Standard s8 [8] | regulatory | overdue: read the gazetted text |
| SA bank 21 / 45 days [10] | practice | overdue |
| Posted-only disputes [15] | practice | 2026-07 (overdue) |
| POPIA s14/s19 [17][18] | regulatory | statute in force; re-check for amendments |

The earliest re-check date has already passed for most regulatory and scheme claims, and the domain pack says to verify regulatory status as current on every load-bearing claim. Before the PRD is locked, confirm the scheme time limits against the primary rules and the Conduct Standard s8 against its gazetted text.
