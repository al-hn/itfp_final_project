# PHONESTeK: Smartphone Pricing and Recommendation System

Introduction to Functional Programming. Assignment 5: Composing Functions Using Pipelining (sections 1 to 9). Assignment 6: Handling Temporal Coupling (section 10). Assignment 7: Exception Handling in FP (section 11).

## 1. Topic, users, team

- **Topic:** select the smartphones that meet a shop's requirements, discount them, and list them from cheapest to most expensive. A secondary `TryTradeIn` function applies a deliberately inconvenient trade-in program.
- **Intended users:** shoppers comparing phones, and shop staff who need a consistent price list.
- Solo work, Alikhan Almasbek IT2513.

## 2. Input

One raw text line per phone: `Model|ReleaseYear|RamGb|StorageGb|BatteryMah|OriginalPrice` (prices in KZT).

| Field | Type | Validation |
|---|---|---|
| Model | string | not empty |
| ReleaseYear | int | whole number, 2000 to 2100 |
| RamGb, StorageGb, BatteryMah | int | whole number, greater than 0 |
| OriginalPrice | decimal | number, greater than 0 (invariant culture, `.` decimal point) |

Parsing happens only at the boundary (`PhoneParser`). Everything after that works with the validated, immutable `Smartphone` record. Missing input (`null`) throws; an empty dataset is valid and produces an empty result. Invalid lines are reported with line number and reason, and no partial result is produced.

## 3. Business rules

**Stage 1, eligibility** (all bounds inclusive). Demo criteria: year >= 2022, RAM >= 8 GB, price from 150,000 to 700,000.

**Stage 2, discount** (percent chosen by a delegate, amount rounded to whole KZT, midpoint away from zero). Reference year is **2026**, passed in explicitly. The code never reads the clock.

| Policy | Rule |
|---|---|
| `standard` | 5% per year of age, capped at 30%. Premium phones (RAM >= 12 GB and storage >= 256 GB) get 5 points less, minimum 0. |
| `clearance` | 8% per year of age, capped at 50%. No premium adjustment. |

**Stage 3, report:** quotes sorted by final price ascending, ties broken by model name.

**Trade-in** (`TradeInProgram.TryTradeIn`): every rule must pass, and all failing reasons are reported.

1. Same brand (first word of the model). iPhone to iPhone, Galaxy to Galaxy.
2. Old phone released **before 2024**.
3. ...but not older than **6 years** (no antiques).
4. New phone at least **2 years newer** than the old one.
5. New phone costs at least **1.5x** the old phone's original price.
6. Old phone battery at least 3000 mAh ("acceptable condition").

Credit = 15% of the old price, capped at 10% of the new price.

## 4. Dataset and documented expected results

Written before running. File: `PHONESTeK/Data/phones.txt` (9 records).

| Phone | Year | RAM | Price | Eligible? | Why |
|---|---|---|---|---|---|
| Galaxy S22 | 2022 | 8 | 350,000 | yes | **boundary**: year and RAM exactly at the minimum |
| iPhone 14 | 2022 | 6 | 500,000 | no | RAM below 8 |
| Pixel 6 | 2021 | 8 | 280,000 | no | year below 2022 |
| iPhone 15 Pro | 2023 | 8 | 600,000 | yes | |
| Galaxy S24 Ultra | 2024 | 12 | 700,000 | yes | **boundary**: price exactly at the maximum; premium |
| Xiaomi 14 | 2024 | 12 | 450,000 | yes | premium |
| Nokia Budget | 2023 | 8 | 120,000 | no | price below 150,000 |
| Galaxy Z Fold 6 | 2024 | 12 | 900,000 | no | price above 700,000 |
| Redmi Note 12 | 2023 | 8 | 150,000 | yes | **boundary**: price exactly at the minimum |

**Expected output, both policies** (original price, discount, final price):

| Phone | Standard | Standard final | Clearance | Clearance final |
|---|---|---|---|---|
| Redmi Note 12 | 15% = 22,500 | **127,500** | 24% = 36,000 | **114,000** |
| Galaxy S22 | 20% = 70,000 | **280,000** | 32% = 112,000 | **238,000** |
| Xiaomi 14 | 5% = 22,500 | **427,500** | 16% = 72,000 | **378,000** |
| iPhone 15 Pro | 15% = 90,000 | **510,000** | 24% = 144,000 | **456,000** |
| Galaxy S24 Ultra | 5% = 35,000 | **665,000** | 16% = 112,000 | **588,000** |
| **Total** | | **2,010,000** | | **1,774,000** |

The pipeline code is identical for both policies. Only the delegate passed in changes the result.

**Trade-in demo** (Part 2 of the program output):

| Old to new | Expected |
|---|---|
| Galaxy S22 to Galaxy S24 Ultra | accepted; credit min(52,500; 70,000) = 52,500; pay 647,500 |
| iPhone 14 to iPhone 15 Pro | rejected: only 1 year newer, and 600,000 < 1.5 x 500,000 |
| Pixel 6 to Xiaomi 14 | rejected: brand mismatch |

**Failure demo** (step 4.11 of the program output): `PHONESTeK/Data/phones_invalid.txt` is rejected with 4 errors: line 2 (`RamGb 'six'`), line 3 (5 fields instead of 6), line 4 (empty model), line 5 (negative price). **Empty demo** (step 4.12): `PHONESTeK/Data/phones_empty.txt` is a valid empty dataset and gives "No phones qualify."

## 5. Pipeline stages

All in `PHONESTeK/PricingStages.cs`, composed in `PricingPipeline.Run`.

| Stage | Function | Input to output | Example |
|---|---|---|---|
| 1 | `SelectEligible` | `IEnumerable<Smartphone>` + `EligibilityCriteria` to `ImmutableArray<Smartphone>` | 9 phones to 5 eligible |
| 2 | `ApplyDiscounts` (higher-order) | `IEnumerable<Smartphone>` + `Func<Smartphone, decimal>` to `ImmutableArray<PhoneQuote>` | Galaxy S22 + standard to 20%, 70,000, final 280,000 |
| 3 | `BuildReport` | `IEnumerable<PhoneQuote>` to `QuoteReport` | 5 quotes sorted by final price, total 2,010,000 |

**Tracing one record (Galaxy S22, standard):** raw text `"Galaxy S22|2022|8|128|3700|350000"` becomes `Smartphone(...)` in the parser, passes Stage 1 (2022 >= 2022, 8 >= 8, price in range), becomes `PhoneQuote(phone, 20, 70000, 280000)` in Stage 2, and is second in `QuoteReport.Quotes` after Stage 3.

**Higher-order function and delegate:** `ApplyDiscounts` takes `Func<Smartphone, decimal> discountPolicy` and calls it with `discountPolicy(phone)` once per phone. The policy is chosen in `Program.RunPricing` (outside the pure core) and passed as an argument. `DiscountPolicies.Standard(year)` and `Clearance(year)` capture only an immutable `int`.

**Purity:** the stages take values and return new immutable values. They do no Console or file access, use no clock or random numbers, and have no static mutable fields. Local builders are never shared. All I/O is in `PHONESTeK` (`Program.cs`, `Presenter.cs`, `PhoneLoader.cs`).

## 6. Requirement map

| Requirement | Where |
|---|---|
| Typed immutable models | `Models.cs` (records, `ImmutableArray`) |
| Boundary parsing and validation | `PhoneParser.TryParseLine` / `ParseAll` |
| 3+ pure stages, composed | `PricingStages.cs`, `PricingPipeline.Run` |
| Higher-order function and two policies | `ApplyDiscounts`, `DiscountPolicies.Standard` / `Clearance` |
| Policy selected outside the pipeline | `Program.RunPricing` |
| I/O separated | `Program.cs`, `Presenter.cs`, `PhoneLoader.cs` |
| Trade-in with `TryTradeIn` | `TradeInProgram.cs` |
| Tests | `PHONESTeK.Tests/PricingWorkflowTests.cs` |

## 7. Tests (xUnit)

| Required case | Test(s) |
|---|---|
| Normal workflow | `NormalWorkflow_StandardPolicy_ProducesDocumentedReport`, `ApplyDiscounts_TenPercentOf500000_Gives450000` |
| Empty dataset | `EmptyDataset_IsValid_AndProducesEmptyReport` |
| Boundary value | `Boundary_ExactThresholdsAreIncluded_JustOutsideIsExcluded`, `Boundary_ZeroDiscountIsLegitimate_FinalEqualsOriginal` |
| Invalid and missing input | `InvalidLine_IsRejected_WithClearMessage` (10 cases), `MissingLine_IsRejected`, `ParseAll_CollectsErrorsWithLineNumbers_AndFabricatesNothing`, `MissingDataset_Throws_NotTreatedAsEmpty`, `PolicyReturningInvalidPercent_IsRejected` |
| Two policies | `TwoPolicies_SameInput_DifferInDocumentedWay`, `HigherOrderStage_InvokesTheDelegate_OncePerPhone` |
| Purity and stages | `Stages_DoNotModifyCallerData_AndResultsAreIndependentCopies`, `SameInputAndPolicy_GiveSameOutput`, `BuildReport_SortsByFinalPrice_TiesByModelName` |
| Trade-in | 5 `TradeIn_*` tests (accepted, multiple reasons, brand, year boundary 2023/2024, too vintage) |

Assignment 5 tests: 29 test cases (19 `[Fact]` + 10 `[InlineData]` rows). Assignment 6 tests are listed in section 10.

**Assignment 7 test outcome:** see the Assignment 8 cumulative test note in Section 12; the suite must be rerun after the Assignment 8 additions.

## 8. Build, run, test

- **.NET SDK:** 10.0.401 on the author's computer (`dotnet --version`). The projects target `net8.0` (`Directory.Build.props`), so a .NET 8 runtime must be installed; to use only a newer runtime, change `TargetFramework` in that one file.
- **Prerequisites:** none besides the SDK. The tests need no network and no files outside the temporary folders they create and delete.

**Build and test** (working directory: the solution folder, where `PHONESTeK.sln` is):

```
dotnet build
dotnet test
```

**Run everything** (working directory: the application folder, `PHONESTeK/PHONESTeK`, where `PHONESTeK.csproj` is). The program takes no parameters:

```
cd PHONESTeK
dotnet run
```

From the solution folder the same thing is `dotnet run --project PHONESTeK`. The program prints four parts in order: 1. pricing pipeline with two policies; 2. trade-in; 3. order workflow (Assignment 6); 4. Result, Maybe, Map/Bind and the file failures (Assignment 7). The data files in `PHONESTeK/Data` are copied next to the executable at build time, so it works from any folder. Exit code: 0 after the demonstrations, 1 if the main catalog `Data/phones.txt` itself cannot be loaded (the reason is printed).

## Project layout

```
PHONESTeK.sln
PHONESTeK/                  application project
  Program.cs                entry point; no parameters, runs all four demonstration parts
  Models.cs                 records (Smartphone, EligibilityCriteria, PhoneQuote, QuoteReport, ...)
  PhoneParser.cs            static: boundary parsing and validation
  PricingStages.cs          static: the three pure stages
  PricingPipeline.cs        static: composes the stages
  DiscountPolicies.cs       static: Standard and Clearance policies
  TradeInProgram.cs         static: TryTradeIn
  ValidatedPurchase.cs      Assignment 6: PurchaseRequest, ValidatedPurchase (factory), PurchaseValidation (Valid/Invalid)
  OrderModels.cs            Assignment 6: OrderState (Quoted/Confirmed/Collected), TransitionResult, OrderBook
  OrderWorkflow.cs          Assignment 6: static OrderTransitions (pure) and OrderService (commit to the book)
  Result.cs                 Assignment 7: AppError, Result<T> (Ok/Fail, Map, Bind), Maybe<T> (Some/None)
  QuoteWorkflow.cs          Assignment 7: PhoneCatalog (lookup), QuoteRequest, QuoteService (the Result chain), QuoteView
  PhoneLoader.cs            Assignment 7: the file/exception boundary (JSON and pipe text)
  Presenter.cs              static: console formatting only
  Data/                     raw input files (phones.txt, phones.json, phones_invalid.txt, phones_empty.txt, phones_malformed.json)
PHONESTeK.Tests/            xUnit tests (PricingWorkflowTests, PurchaseWorkflowTests, ResultAndQuoteTests, FileBoundaryTests, TestData)
```

## 9. Team member contribution

| Member | Implemented logic | Tests | Documentation |
|---|---|---|---|
| `<YOUR NAME>` (`<GROUP>`) | everything above | everything above | this README |

## 10. Assignment 6: Handling temporal coupling

### Workflow, input, rules, output

The new workflow is **placing and fulfilling an order** for a phone from the catalog (`Data/phones.txt`, same input format as before). A customer request (`PurchaseRequest`: customer, model, optional trade-in model) is validated, priced, stored as a *Quoted* order, then *Confirmed*, then *Collected* (handed over).

Validation rules (all checked together, all reasons reported): 1. customer name not blank; 2. model exists in the catalog; 3. phone meets the shop criteria (year >= 2022, RAM >= 8 GB, price 150,000 to 700,000); 4. if a trade-in phone is given, it exists and passes every `TryTradeIn` rule from section 3; 5. discount plus credit leave something to pay. Amount to pay = original price - standard discount - trade-in credit.

State rules: Quoted -> Confirmed (needs a confirmation number) -> Collected. Anything else is rejected with a reason. **Repeated operations:** confirming an already confirmed order returns the *existing* confirmation (same state, no new number, nothing stored again); collecting twice, or confirming a collected order, is rejected.

### Task 1: the hidden dependency (unsafe "before", documentation only)

This code is **not in the project**. It shows the old style, where a result depends on calling steps in a hidden order:

```csharp
public class UnsafeOrder
{
    private EligibilityCriteria? _criteria;          // filled by LoadRules()
    private Func<Smartphone, decimal>? _policy;      // filled by LoadRules()
    public Smartphone? Phone;
    public bool IsValid;                             // filled by Validate()
    public decimal Total;                            // filled by Calculate()
    public bool Confirmed;

    public void LoadRules(EligibilityCriteria c, Func<Smartphone, decimal> p) { _criteria = c; _policy = p; }
    public void Validate() => IsValid = Phone != null && _criteria != null && PricingStages.IsEligible(Phone, _criteria);
    public void Calculate() => Total = Phone!.OriginalPrice - (_policy == null ? 0 : Phone.OriginalPrice * _policy(Phone) / 100);
    public void Confirm() => Confirmed = true;       // never looks at IsValid
}
```

Required order: `LoadRules()` -> set `Phone` -> `Validate()` -> `Calculate()` -> `Confirm()`. Nothing in the API says so.

| What the caller does | What goes wrong (observable) |
|---|---|
| Forgets `LoadRules()` | `Calculate()` silently skips the discount: Galaxy S22 costs 350,000 instead of 280,000. `Validate()` returns false for every phone. |
| Skips `Validate()` | `Confirm()` succeeds for Nokia Budget (120,000, below the price range): an invalid confirmation. |
| Calls `Confirm()` before `Calculate()` | A confirmed order with `Total = 0`. |
| Calls `Confirm()` twice | Succeeds silently again; nothing says which confirmation is the real one. |

### Task 2: validated factory (`ValidatedPurchase.Create`)

`ValidatedPurchase.Create(request, catalog, criteria, policy, referenceYear)` checks the five rules above and returns either `Valid` (carrying the finished value) or `Invalid` (carrying every reason). Nothing partial escapes.

**Access restrictions:** the constructor is `private`; the class is `sealed` (no subclass can skip checks); all properties are get-only; it is a class, not a record, so there is no `with` copy that could change a field. The only way to get one is `Create`. The amounts (discount, credit, total) are computed inside the factory from the rules that were checked, so they cannot be set to inconsistent values. The factory is used by the real workflow: `OrderService.Place` calls it, and `Program.RunOrders` calls `OrderService.Place`.

### Task 3: the cases

| Type | Case | Meaning | Carries |
|---|---|---|---|
| `PurchaseValidation` | `Valid` | the request passed every rule | a `ValidatedPurchase` |
| | `Invalid` | at least one rule failed | `ImmutableArray<string>` of reasons |
| `OrderState` | `Quoted` | validated, priced, not yet confirmed | order id, `ValidatedPurchase` |
| | `Confirmed` | confirmed by the shop | order id, `ValidatedPurchase`, confirmation number |
| | `Collected` | phone handed over | order id, `ValidatedPurchase`, confirmation number |
| `TransitionResult` | `Moved` | the transition succeeded | the next `OrderState` |
| | `Rejected` | the transition is not allowed | the reason |

These are closed record hierarchies: each base type has a private constructor, so no other case can be declared outside it. `OrderState` case constructors are `internal`, so states are created only by `OrderTransitions`.

### Task 4: pattern matching in the real path

- `OrderService.Place` matches the factory outcome: `Valid` -> add a Quoted order to a new book; `Invalid` -> reject with the joined reasons and return the old book.
- `OrderTransitions.Confirm` / `Collect` match the current `OrderState`, one explicit branch per case. The final `_` branch rejects an unexpected (even null) state, never treats it as valid.
- `OrderService.Confirm` / `Collect` match the `TransitionResult` before committing; `Presenter.PrintStep` / `Describe` match results and states for output.

No casts and no `!` are used to reach a payload: each payload is only reachable inside the branch for its case.

### Task 5: safe operations

- **Visible prerequisites:** `OrderTransitions.Quote` takes a `ValidatedPurchase`, not a raw request; `Confirm` and `Collect` take an `OrderState` and say in their switch which states are allowed; the confirmation number is an explicit input. Collecting before confirming returns `Rejected` with a clear message.
- **Purity:** `ValidatedPurchase.Create` and `OrderTransitions` do no I/O, use no clock and mutate nothing. `OrderService` returns a new `OrderBook` (immutable) or the **same instance** when a step is rejected, so a failure cannot leave a half-updated book. All prerequisites are checked before the one `book with { ... }` commit.
- **Calls prevented at compile time (not in the code):** `new ValidatedPurchase(...)` (private constructor); `OrderTransitions.Quote("ORD-1", rawRequest)` (needs a `ValidatedPurchase`, not a `PurchaseRequest`); `new OrderState.Confirmed(...)` from outside the project (internal constructor); `new PurchaseValidation.Valid(somethingUnchecked)` (needs a `ValidatedPurchase`); declaring a new case of `OrderState` (private base constructor).

### Before and after at the call site

```csharp
// BEFORE (unsafe, hidden order)
var order = new UnsafeOrder { Phone = phone };
order.Confirm();                       // compiles, "works", nothing was validated

// AFTER (safe, prerequisites visible)
var (book2, result) = OrderService.Place(book, request, catalog, criteria, policy, year); // validates inside
// result is Moved(Quoted) or Rejected(reasons); the book only changes on Moved
var (book3, confirmed) = OrderService.Confirm(book2, "ORD-0001");   // rejects unknown or already-collected orders
```

### Demonstration (Part 3 of `dotnet run`)

Expected output (written before running; each step shows its result; the program numbers them 3.1 to 3.10):

| Step | Action | Expected |
|---|---|---|
| 1 | Place: Aigerim, Galaxy S22 | OK, ORD-0001 quoted, discount 70,000, credit 0, pay 280,000 |
| 2 | Collect ORD-0001 before confirming | REJECTED: "has not been confirmed yet" (invalid sequence) |
| 3 | Confirm ORD-0001 | OK, confirmation #1001 |
| 4 | Confirm ORD-0001 again | OK, the same confirmation #1001 (repeat rule) |
| 5 | Collect ORD-0001 | OK, collected, confirmation #1001 |
| 6 | Collect ORD-0001 again | REJECTED: "already collected" |
| 7 | Place: Madina, Nokia Budget | REJECTED: price 120000 is outside 150000..700000 |
| 8 | Place: Timur, iPhone 15 Pro, trade-in iPhone 14 | REJECTED: trade-in rules (gap is 1; price below 1.5x) |
| 9 | Place: Daniyar, Galaxy S24 Ultra, trade-in Galaxy S22 | OK, ORD-0002 quoted, discount 35,000, credit 52,500, pay 612,500 |
| 10 | Confirm ORD-9999 | REJECTED: order does not exist |

Final order book: 2 orders (ORD-0001 collected, ORD-0002 quoted). Rejected steps never change the book, and the failed placements in steps 7 and 8 do not use up order numbers (the next order is ORD-0002).

### Requirement map

| Requirement | Where |
|---|---|
| Task 1: unsafe before example, not active | section 10 above (documentation only) |
| Task 2: validated factory, used by the app | `ValidatedPurchase.Create` (ValidatedPurchase.cs); used by `OrderService.Place` |
| Task 2: unchecked construction prevented | private constructor, sealed class, get-only properties |
| Task 3: distinct cases | `PurchaseValidation`, `OrderState`, `TransitionResult` |
| Task 4: pattern matching in the domain | `OrderService.Place`, `OrderTransitions.Confirm`, `OrderTransitions.Collect`, `OrderService.Confirm` / `Collect` |
| Task 5: safe, pure, state preserved, repeats defined | `OrderTransitions`, `OrderService`, immutable `OrderBook` |
| Demonstration | `Program.RunOrders` |

### Assignment 6 tests (`PurchaseWorkflowTests.cs`: 20 `[Fact]` + 11 `[InlineData]` = 31 test cases)

| Required case | Test(s) |
|---|---|
| Factory success | `Factory_ValidRequest_CreatesUsableValidatedPurchase`, `Factory_ValidRequestWithTradeIn_AppliesDiscountThenCredit`, `Factory_TrimsCustomerName_AndMatchesModelIgnoringCase` |
| Factory rejection (each rule) | `Factory_InvalidRequest_IsRejected_WithTheReason` (11 rows: blank customer, unknown model, year, RAM, price low and high, unknown trade-in, trade-in rules, brand), `Factory_ReportsAllReasonsAtOnce`, `Factory_MissingInput_Throws` |
| Distinct cases | `ValidationCases_ValidAndInvalid_ProduceDifferentWorkflowOutcomes`, `OrderStates_EveryCase_IsHandledExplicitlyByEveryTransition`, `UnknownState_IsRejected_NeverTreatedAsSuccess` |
| Normal operation | `NormalOperation_PlaceConfirmCollect_MovesThroughEveryState`, `NormalOperation_OrderNumbersAreSequential` |
| Missing prerequisite | `MissingPrerequisite_CollectBeforeConfirm_IsRejected_AndBookIsUnchanged`, `MissingPrerequisite_UnknownOrder_IsRejected_ForConfirmAndCollect` |
| Repeated operation | `RepeatedConfirm_ReturnsTheExistingConfirmation_WithoutUsingAnotherNumber`, `RepeatedCollect_IsRejected_AndBookIsUnchanged`, `ConfirmAfterCollect_IsRejected_AndStaysCollected` |
| Boundary value | `Boundary_PriceExactlyAtLimits_IsAccepted_OneUnitOutsideIsRejected` (700,000 and 150,000 accepted; 700,001 and 149,999 rejected), `Boundary_RamAndYearExactlyAtLimits_AreAccepted` |
| State preservation | `FailedPlace_LeavesTheOriginalBookUnchanged_AndBurnsNoOrderNumber`, `SuccessfulOperations_DoNotModifyTheEarlierBook`, `FailedPlace_DoesNotModifyTheCallersCatalog` |

Cumulative: 29 (Assignment 5) + 31 (Assignment 6) = **60 test cases**, well above the minimum of 8. **Latest test outcome:** `<fill in after running>`.


## 11. Assignment 7: Exception handling in FP

### Workflow, input, rules, output

The new workflow is a **price quote**. A request has a model, a quantity (1 to 5) and an optional budget. The chain looks the phone up, validates the request, prices it with the existing discount stage (standard policy), and builds a display model. Phone data can come from the pipe-delimited text file (as before) or from JSON (`Data/phones.json`, an array of objects with `model`, `releaseYear`, `ramGb`, `storageGb`, `batteryMah`, `originalPrice`). Both go through the same validation rules (`PhoneParser`). Earlier parts stay active: the pricing pipeline (Part 1), trade-in (Part 2) and the safe order states (Part 3) still run in the same program.

What each type means (they are different things and are kept separate):

| Type | Meaning | Cases |
|---|---|---|
| `Result<T>` | the outcome of one operation that may fail for an expected reason | `Ok(value)`, `Fail(AppError)` |
| `AppError` | why it failed: stable `Code`, readable `Message`, and `Kind` (`Domain` = business rule, `Operational` = file or environment) | one record |
| `Maybe<T>` | a lookup that may find nothing, or an optional input | `Some(value)`, `None` |
| `PurchaseValidation` (Assignment 6) | result of validating an order request, listing ALL reasons | `Valid`, `Invalid` |
| `OrderState` (Assignment 6) | where a stored order is in its life | `Quoted`, `Confirmed`, `Collected` |

A validation failure is a decision about a request (`Invalid` says "this request cannot become an order"); a workflow state is a fact about an order that already exists. `Result` is the general success-or-failure wrapper used to compose steps. `Maybe` is only about presence.

### Task 1: explicit outcomes (`Result.cs`)

`Result<T>` has a private base constructor (closed hierarchy) and `internal` case constructors. `Ok` rejects a null payload, and `Fail` has no `Value` property at all, so a failure cannot be read as a success and a success cannot lack its value. Expected failures never use exceptions. Four domain failures of the quote chain, with the expected result of each (`QuoteService.Quote`, standard policy, year 2026):

| Case | Request | Expected |
|---|---|---|
| Success | 2 x Galaxy S22, no budget | `Ok(QuoteView("Galaxy S22", 2, "20%", "280,000", "560,000"))` |
| `invalid_quantity` | quantity 0, -1 or 6 | `Fail`, Domain: "Quantity must be between 1 and 5 (got 0)." |
| `not_eligible` | Nokia Budget (price below the range) | `Fail`, Domain: "'Nokia Budget' does not meet the shop requirements (year >= 2022, RAM >= 8 GB, price 150000..700000)." |
| `over_budget` | 2 x Galaxy S22, budget 500,000 | `Fail`, Domain: "Total 560,000 for 2 x 'Galaxy S22' exceeds the budget of 500,000." |
| `phone_not_found` | Galaxy S99 | `Fail`, Domain: "Model 'Galaxy S99' is not in the catalog." |

Every error names the rule or item involved; an empty successful result is never returned for a failure.

### Task 2: missing values (`Maybe`, `PhoneCatalog.Find`)

`PhoneCatalog.Find(catalog, model)` returns `Some(phone)` or `None` (matching ignores case and surrounding spaces; a blank or null name is `None`). The phone is **required**, so the chain converts `None` into a failure with `ToResult` (`phone_not_found`). The budget is **optional**: `None` means no limit, and `Some(0)` is a real budget of zero, which fails with `over_budget` (zero is data, not absence; tested). `Some` rejects null, no `default(T)` is used as proof of absence, and there is no `!` operator. `ValidatedPurchase` (Assignment 6) uses the same `PhoneCatalog.Find`, so there is one lookup rule.

### Task 3: Map and Bind in one real chain (`QuoteService.Quote`)

```csharp
PhoneCatalog.Find(catalog, request.Model)                  // Maybe<Smartphone>
    .ToResult(AppError.Domain(PhoneNotFound, ...))         // Result<Smartphone>
    .Bind(phone => Validate(phone, request, criteria))     // Smartphone -> Result<ValidQuoteRequest>  (can fail)
    .Bind(valid => Price(valid, discountPolicy))           // ValidQuoteRequest -> Result<PriceQuote>  (can fail)
    .Map(QuoteView.From);                                  // PriceQuote -> QuoteView                  (cannot fail)
```

`Bind` takes a function that itself returns a `Result` (validation and pricing can reject), and its result is kept as it is. `Map` takes a function that returns a plain value (building the display model cannot fail), and wraps it in `Ok`. That is why their signatures differ: `Map : Result<T> + (T -> U) -> Result<U>` and `Bind : Result<T> + (T -> Result<U>) -> Result<U>`. On a failure both return the same error and **do not call** the supplied function. The final outcome is handled once, at the boundary, by `Presenter.PrintOutcome` (a `switch` with one branch per case).

### Task 4: exception boundary (`PhoneLoader.cs`)

`PhoneLoader.LoadPipeText(path)` and `LoadJson(path)` are the only place where file and parsing exceptions are caught; file access stays out of the pure functions. Each exception becomes a `Fail` of kind `Operational` that names the file:

| Situation | Caught | Code |
|---|---|---|
| file or folder does not exist | `FileNotFoundException`, `DirectoryNotFoundException` | `file_missing` |
| access denied, a folder instead of a file, other I/O error | `UnauthorizedAccessException`, `IOException` | `file_unreadable` |
| empty or unsupported path | `ArgumentException`, `NotSupportedException` | `file_path_invalid` |
| text is not valid JSON, or is not an array of phones | `JsonException` (and a null array) | `file_malformed` |
| valid JSON but bad values or missing fields; invalid lines in a text file | (no exception, validation) | `file_malformed`, listing every bad entry or line |

Domain failures (`ErrorKind.Domain`) are expected business outcomes; operational failures (`ErrorKind.Operational`) are about files. There are no empty catch blocks and no fallback: nothing pretends a failed load succeeded. A load is all-or-nothing (a failure carries no phones), so a bad file cannot leave a partial catalog; data that was loaded earlier is untouched (tested). If the main catalog cannot be loaded at startup, the program prints the reason and exits with code 1.

### Task 5: demonstration (Part 4 of `dotnet run`)

| Step | Case | Expected output |
|---|---|---|
| 4.1 | Load `Data/phones.json` (normal) | OK, 9 phone(s) loaded |
| 4.2 | Normal path: 2 x Galaxy S22, no budget | OK, 20% off, 280,000 each, total 560,000 KZT |
| 4.3 | Domain failure: quantity 0 | FAILED [Domain: invalid_quantity] |
| 4.4 | Domain failure: Nokia Budget | FAILED [Domain: not_eligible] |
| 4.5 | Domain failure: budget 500,000 for a 560,000 total | FAILED [Domain: over_budget] |
| 4.6 | Lookup absent: Galaxy S99 | FAILED [Domain: phone_not_found] |
| 4.7 | Lookup present (typed `galaxy s22`), budget exactly 560,000 | OK, total 560,000 (boundary allowed) |
| 4.8 | Budget 0 | FAILED [Domain: over_budget] (zero is a real budget) |
| 4.9 | Missing file `phones_does_not_exist.json` | FAILED [Operational: file_missing] with the path |
| 4.10 | Malformed JSON `phones_malformed.json` | FAILED [Operational: file_malformed], "not valid JSON" |
| 4.11 | Invalid text file `phones_invalid.txt` | FAILED [Operational: file_malformed], 4 invalid lines, nothing loaded |
| 4.12 | Empty text file `phones_empty.txt` | OK, 0 phone(s) (valid empty dataset), then "No phones qualify." |

Repeated operations and invalid sequences of the order workflow (Part 3) are unchanged and still demonstrated.

### Requirement map

| Requirement | Where |
|---|---|
| Task 1: Result with distinct cases, protected against contradictions | `Result<T>`, `AppError`, `ErrorCodes` (Result.cs) |
| Task 1: at least three domain failures | `QuoteService.Validate` (`invalid_quantity`, `not_eligible`), `QuoteService.Price` / `CheckBudget` (`over_budget`), lookup (`phone_not_found`) |
| Task 2: Maybe, real lookup, present and absent | `Maybe<T>`, `PhoneCatalog.Find`, optional `QuoteRequest.Budget` |
| Task 3: Map and Bind in a real chain | `QuoteService.Quote`; `Result<T>.Map` / `Bind` |
| Task 3: handled once at the boundary | `Presenter.PrintOutcome` |
| Task 4: file and malformed content at the boundary | `PhoneLoader.ReadFile`, `ParseLines`, `ParseJson` |
| Task 5: integration, earlier parts still active | `Program.RunAll` (Parts 1 to 4), `RunResults` |
| Single `dotnet run` | `Program.Main()` without parameters |

### Assignment 7 tests (53 test cases: 33 `[Fact]` + 20 `[InlineData]` rows)

| Required case | Test(s) |
|---|---|
| Complete success | `Quote_CompleteSuccess_ProducesTheDisplayModel`, `Quote_QuantityBoundaries_OneAndFiveAreAccepted`, `FileToQuote_CompleteChain_LoadsThenQuotes` |
| Domain failures with useful errors | `Quote_InvalidQuantity_FailsWithTheRuleAndTheValue` (3 rows), `Quote_NotEligible_FailsAndNamesThePhoneAndTheRule` (3 rows), `Quote_OverBudget_FailsAndReportsTotalAndBudget`, `Quote_UnknownModel_FailsAsAMissingLookup` |
| Lookup present and absent | `PhoneCatalog_Find_HandlesPresentAndAbsent`, `Maybe_PrimitiveValuesThatLookEmpty_ArePresent_NotAbsent`, `Maybe_ToResult_PresentBecomesOk_AbsentBecomesTheGivenFailure`, `Quote_BudgetAbsent_MeansNoLimit`, `Quote_BudgetZero_IsARealLimit_NotAbsence`, `Quote_BudgetEqualToTheTotal_IsAccepted_OneLessIsRejected` |
| Map success | `Map_Success_PassesTheValueToTheFunction_AndWrapsTheNewValue` |
| Bind success | `Bind_Success_PassesTheValue_AndKeepsTheInnerResult` |
| Failure propagation (functions not invoked) | `Map_Failure_IsPropagated_AndTheFunctionIsNotCalled`, `Bind_Failure_IsPropagated_AndTheFunctionIsNotCalled`, `Chain_StopsAtTheFirstFailure_AndSkipsEveryLaterStep` (call log), `Quote_AfterAFailure_ThePricingStepIsNeverCalled` (3 rows, counter), `Quote_OnSuccess_ThePricingStepIsCalledExactlyOnce` (control) |
| Missing file, state preserved | `LoadJson_MissingFile_ReportsAnOperationalFailure_AndCreatesNothing`, `LoadPipeText_MissingFile_ReportsAnOperationalFailure`, `MissingFile_LeavesAnAlreadyLoadedCatalogUnchanged_AndSkipsTheDependentStep`, `LoadJson_DirectoryInsteadOfFile_IsReportedAsUnreadable`, `Load_EmptyPath_IsReportedAsAnInvalidPath` |
| Malformed file, no partial update | `LoadJson_BrokenSyntax_BecomesAClearFailure_WithoutAnyPhones`, `LoadJson_ContentThatIsNotAPhoneArray_IsMalformed` (5 rows), `LoadJson_WrongValueType_IsMalformed`, `LoadJson_InvalidEntries_AreAllReported_AndNothingIsLoaded`, `LoadPipeText_InvalidLines_AreReportedWithLineNumbers_AndNothingIsLoaded` |
| Success of the loaders, empty data, shared rules | `LoadJson_ValidFile_ReturnsAllPhonesWithTheirData`, `LoadJson_EmptyArray_IsAValidEmptyDataset`, `LoadPipeText_ValidAndEmptyFiles_AreSuccesses`, `TryCreate_AppliesTheSameRulesAsTheTextParser` (4 rows), `TryCreate_BoundaryYear2000_IsAccepted` |
| Result model | `Result_Ok_CarriesItsValue`, `Result_Fail_CarriesItsError_AndIsNotASuccess`, `Result_CannotBeBuiltWithMissingParts`, `Maybe_Some_RejectsNull` |

File tests (`FileBoundaryTests`) create their own temporary folder and delete it afterwards; they do not use any file that already exists on the computer.

Before Assignment 8, the README counted 113 cumulative Assignment 5–7 test cases. Assignment 8 adds 18 test methods; see Section 12 for the updated coverage and test execution limitation.

### Contribution (Assignment 7)

Solo work, Alikhan Almasbek IT2513: Result and Maybe, the quote chain, the file loader and boundary, the single-run `Program`, tests and this documentation.

---

## 12. Assignment 8: extension methods, LINQ, currying and native C

Assignment 8 extends this same console application. `dotnet run` still starts at `Program.Main()` and runs Assignments 5–8 in sequence; no GUI, server, or second application was introduced.

### Task 1 — custom extension methods (`Assignment8.cs`)

- `PhoneQuote.Savings()` returns `OriginalPrice - FinalPrice`. A quote is the natural receiver because it contains both prices needed to calculate the saving; callers do not need to pass a separate phone and price.
- `IEnumerable<PhoneQuote>.OrderByFinalPriceThenModel()` orders quotes by final price and then model name using ordinal comparison. The sequence is the receiver because ordering is an operation on a collection, and the explicit tie-breaker makes equal-price output deterministic.

Both are called by `Program.RunAssignment8`: each quote displays its saving, and the report uses the ordering extension.

### Task 2 — two LINQ reports (`PhoneReports`)

Both reports use the validated phone catalog loaded from `Data/phones.json` for the Assignment 8 workflow. Query logic returns immutable data; `Program.RunAssignment8FromJson` loads the JSON input and `Program.RunAssignment8` handles Console output.

1. **Eligible phone list.** Input: the loaded catalog and the existing `EligibilityCriteria`. Intended user: shopper or sales staff. Filter: release year, RAM, and inclusive price range. Output: model, price, release year, and RAM; ordered by price ascending, then model ordinal ascending. Empty input and no matches produce an empty report.
2. **Catalog value by model prefix.** Input: the same loaded catalog. Intended user: shop manager. Grouping: first word of `Model` (for this dataset, a simple manufacturer label). Output: label, phone count, sum of original prices, and average original price; ordered by total value descending, then label ordinal ascending.

**Expected results for the supplied JSON:** Report 1 returns `Redmi Note 12` (150,000), `Galaxy S22` (350,000), `Xiaomi 14` (450,000), `iPhone 15 Pro` (600,000), and `Galaxy S24 Ultra` (700,000), in that order. Report 2 returns `Galaxy` (3 phones, 1,950,000 total, 650,000 average), `iPhone` (2, 1,100,000, 550,000), `Xiaomi` (1, 450,000, 450,000), `Pixel` (1, 280,000, 280,000), `Redmi` (1, 150,000, 150,000), and `Nokia` (1, 120,000, 120,000), in that order.

Across the reports the implementation uses `Where`, `Select`, `OrderBy`, `OrderByDescending`, `ThenBy`, `GroupBy`, `Count`, `Sum`, and `Average`. The eligible report composes filtering, projection, ordering, and materialisation. The second report groups and aggregates. Equal sort values are resolved with ordinal text keys.

### Task 3 — currying and partial application (`PhoneScoring`)

`CreateScore` has the curried shape `PhoneScoreConfiguration -> (Smartphone -> int)`. It first accepts a configuration and returns a scoring function. `Balanced` and `BatteryFocused` are partially applied functions created by fixing different weights. Both are run on the same first catalog phone in the application, and the test verifies that their scores differ. The configuration is passed as a value; no global setting is mutated.

### Task 4 — deferred execution and snapshot (`PhoneStreams`)

`EligibleDeferred` is an `IEnumerable<Smartphone>` iterator implemented with `yield return`. Creating the enumerable does not enumerate the source. The source probe in `Program.RunAssignment8` demonstrates zero processed records before iteration; `ToArray()` is the deliberate snapshot point. Enumerating the deferred query again would re-run the in-memory filtering and source enumeration. Enumerating the array again only traverses the already materialised snapshot and does not re-read the input file. The query is over the already-loaded immutable catalog, not an open file or a business-state transition.

### Tasks 5–6 — resource ownership and retention

`PhoneLoader.LoadPipeText` uses `File.ReadAllLines`; `LoadJson` uses `File.ReadAllText`. These convenience APIs own their internal file handles and close them before returning the loaded string/line array. `PhoneLoader` owns the call boundary; the caller receives a completed immutable snapshot, not a live stream. On exceptions, the convenience API releases its internal handle as part of the API's file operation before the loader converts the exception to an operational `Result` failure. No extra stream was created merely to demonstrate `using`.

The main phone catalog is an `ImmutableArray<Smartphone>` snapshot. The deferred report iterator temporarily retains its source enumerable while it is being enumerated; it is not stored in a static cache or event subscription. `ToArray()` is used where a stable repeated-use snapshot is useful. The trade-off is that streaming avoids an additional result collection but repeated enumeration repeats in-memory work; materialisation allocates and retains an array but makes later traversals predictable and avoids repeating the query. There is no application cache or event-subscription collection in this project.

### Tasks 7–9 — native C integration

Files:

- `native/phonestek_native.c` — exported C implementation.
- `native/phonestek_native.h` — function declaration.
- `native/build-linux.sh` — reproducible Linux x64 build using GCC.
- `native/build-windows.ps1` — optional Windows x64 build using MSVC from an x64 Native Tools Command Prompt.
- `PHONESTeK/Assignment8.cs` — `DllImport` declaration and `NativePhoneScorer` adapter.
- `PHONESTeK.Tests/Assignment8Tests.cs` — real-call expected-value test and controlled adapter-failure test.

**Native contract:** `int32_t phonestek_phone_score(int32_t ram_gb, int32_t storage_gb, int32_t battery_mah)`. Calling convention is C ABI / `CallingConvention.Cdecl`. Valid ranges are RAM 1–1024 GB, storage 1–1,000,000 GB, and battery 1–100,000 mAh. C returns `-1` for invalid input. The C# adapter validates the same ranges before calling, so invalid domain input becomes `native_input_invalid` and never enters native code. Library, export, or architecture loading failures become operational `native_unavailable` results. The function uses integer arithmetic and allocates no native memory.

**Calculation and manual check:** `RAM * 100 + storage / 10 + battery / 100`, with integer division. For the first sample phone (8 GB RAM, 128 GB storage, 3700 mAh): `8*100 + 128/10 + 3700/100 = 800 + 12 + 37 = 849`. The application invokes the real native function through P/Invoke and reports its result.

**Native build tested in this environment:** Debian GNU/Linux 13 (trixie), x86_64, GCC 14.2.0. From the extracted project root:

```sh
./native/build-linux.sh
dotnet run --project PHONESTeK/PHONESTeK.csproj
dotnet test PHONESTeK.sln
```

The script builds `runtimes/linux-x64/native/libphonestek_native.so`. The project files copy runtime binaries to the application/test output directory root, where the default .NET native library resolver can find `libphonestek_native.so`. The native binary is included in the submitted project ZIP; rebuilding it is reproducible with the script. The C compiler is required only when rebuilding the native library. .NET SDK target is net8.0 as configured in `Directory.Build.props`.

**Integration difficulty and resolution:** a native library in a nested runtime folder is not guaranteed to be found by a plain `DllImport` unless the runtime asset is configured. The project build items therefore copy the platform library to the managed output directory root. The adapter separately handles missing-library, missing-export, and wrong-architecture failures as explicit `Result` outcomes.

### Tests and coverage

`Assignment8Tests.cs` adds coverage for normal LINQ output, empty input, no matches, deterministic ties, grouping and totals, both extensions, both configured functions on the same input, deferred execution, snapshot reuse, a manually checked native score, native adapter failure, invalid native input, and an integrated report/scoring workflow. The native P/Invoke test exercises the actual compiled C library; the injected failure test checks the adapter's operational-error conversion.

The pre-existing Assignment 5–7 test files are retained unchanged. Assignment 8 contributes 18 additional test methods, bringing the documented cumulative count to 131 test cases (113 earlier + 18 new). The two file-cleanup tests verify that file handles are available for move/delete after both successful and malformed reads. The test project remains xUnit-based and cumulative. Run `dotnet test PHONESTeK.sln` from the project root to get the authoritative count and outcome. **This execution environment does not have the .NET SDK installed, so the C# build and xUnit suite could not be run here; do not treat the source-level test expectations as a passing test run.** The Linux native C library itself was built with GCC 14.2.0 and its exported symbol and manually checked result (849) were verified. The C# application and xUnit tests remain unexecuted here.

### Requirement-to-code map

| Assignment 8 requirement | Implementation |
|---|---|
| Two used extensions | `PhoneExtensions.Savings`, `OrderByFinalPriceThenModel`; called in `Program.RunAssignment8` |
| Two LINQ reports | `PhoneReports.EligibleCatalog`, `ValueByBrand` |
| Currying and partial application | `PhoneScoring.CreateScore`, `Balanced`, `BatteryFocused` |
| Deferred execution and materialisation | `PhoneStreams.EligibleDeferred`, `ToArray()` in `Program.RunAssignment8` |
| Resource ownership | `PhoneLoader.LoadPipeText`, `LoadJson` convenience APIs |
| Retention/allocation note | This README, Tasks 5–6 |
| Native C export and build | `native/phonestek_native.c`, `.h`, build scripts |
| P/Invoke and Result adapter | `NativePhoneScorer` in `Assignment8.cs` |
| Real workflow call | `Program.RunAssignment8` |
| Tests | `PHONESTeK.Tests/Assignment8Tests.cs` plus retained earlier test suites |

### Contribution

Solo work, Alikhan Almasbek IT2513. Assignment 8 additions: extension methods, LINQ report functions, curried scoring configurations, deferred iterator, C native function/build scripts, P/Invoke adapter, integration in the existing `Main()` workflow, tests, and this documentation.
