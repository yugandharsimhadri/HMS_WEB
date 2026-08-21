# Sprint 1 — Step 1: Requirements Specification

**Project:** Sivayaan Content Engine
**Feature:** Content Automation Workflow
**Step:** 1 — Create Workflow Execution
**Date:** 2026-08-02
**Status:** Draft for review
**Activity type:** Requirements only. No design, no implementation, no technology decisions.

**Discovery inputs referenced:**
- `docs/discovery/Step01_UAT_Discovery.md` (how UAT runs today)
- `docs/discovery/Step02_TestCatalog.md` (what tests exist)
- `docs/discovery/Step03_ProductRegistration.md` (what identifies the product)

---

## 1. Purpose

Step 1 gives the Content Engine the ability to **create a Workflow Execution**: a uniquely identified, durable record that captures a user's intent to run a Content Automation Workflow against a registered product, together with everything a later step will need to carry that intent out.

Creating an execution and carrying it out are deliberately separate. Discovery established that actually running UAT has heavy environmental preconditions (a built product, an interactive desktop, exclusive use of the machine) and destructive members if invoked carelessly. Step 1 therefore records *what should happen* — it must never cause anything to happen. This separation lets intent be captured, reviewed, and audited independently of when (or whether) the machine is ready to act on it.

## 2. Scope

Step 1 covers, and only covers:

1. Accepting a request to create a Workflow Execution for a registered product.
2. Validating that request against the information the Content Engine holds about the product and its test inventory.
3. Recording the execution as a new, uniquely identified instance in a defined initial state.
4. Reporting the outcome of the creation attempt (created, or rejected with reasons) back to the requester.
5. Making the created execution retrievable afterwards (by its identifier) so later steps and human reviewers can see it.

## 3. Out of Scope

- Executing, scheduling, queueing, pausing, resuming, or cancelling any UAT test or any other work. (Cancellation of a *created but never started* execution is noted as an Open Question, not a Step 1 requirement.)
- Building the product, launching the product, or touching the product's machine environment in any way.
- Collecting, parsing, or presenting test results, screenshots, logs, or any generated content.
- Product registration itself (assumed to precede this step; its own requirements are separate).
- Defining or editing workflow *definitions/templates* (what a reusable workflow "is" — see Open Questions).
- User management, authentication, and authorization schemes (an actor model is stated below; its enforcement mechanism is not specified here).
- Notifications to third parties.
- Any user interface layout, storage design, data format, or technology choice.

## 4. Actors

| Actor | Role in Step 1 |
|---|---|
| **Requester** (human operator of the Content Engine) | Initiates creation of a Workflow Execution; supplies the user inputs; receives the outcome. |
| **Content Engine** (the system) | Validates the request, creates and persists the execution record, assigns its identity and initial state, reports the outcome. |
| **Downstream workflow steps** (future sprints; not active in Step 1) | Consumers of the created execution. Named here only to fix the requirement that the record must be sufficient for them; they perform no action in Step 1. |

No other actors participate. The product under test (HMS_WPF / TwinkleHMS) is **not** an actor in Step 1 — it is data referenced by the execution.

## 5. Preconditions

1. The product has been registered with the Content Engine (per Step 1.2 discovery, registration establishes at minimum: the product's identity, its repository/solution location, its UAT test project, and its executable identity).
2. A test inventory for the product is available to validate a requested test selection against. Discovery established that the only trustworthy inventory is one discovered from the product's current source/build, because test names have already drifted once from recorded evidence; whether the inventory is refreshed at creation time or accepted as-of-registration is an Open Question.
3. The requester is known to the Content Engine (at minimum, identifiable for the audit record).

Note deliberately absent from preconditions: the product's machine environment (built solution, free desktop, tooling present) is **not** required to create an execution — those are preconditions of *running* one, which is out of scope.

## 6. Functional Requirements

| ID | Requirement |
|---|---|
| FR-01 | The system shall allow a requester to create a Workflow Execution for exactly one registered product per execution. |
| FR-02 | The system shall reject a creation request that references a product not registered with the Content Engine. |
| FR-03 | The system shall capture, as part of the execution, the scope of work requested: which tests (or groups of tests) of the product's UAT suite the execution is intended to cover. Selection granularities that must be expressible follow the granularities that exist today in the product (per Step 1.1 discovery): the whole UAT suite, one test class, or one individual test. |
| FR-04 | The system shall validate a requested test selection against the product's test inventory and reject selections that reference tests unknown to that inventory. |
| FR-05 | The system shall assign every created execution an identifier that is unique across all executions of all products, and shall never reuse an identifier. |
| FR-06 | The system shall place every created execution in a single, defined initial state (working name: **Created**) that unambiguously means "recorded but not started". |
| FR-07 | The system shall record with the execution: the requester's identity, the date and time of creation, the product referenced (including the product version information available at creation time), and the requested test scope. |
| FR-08 | The system shall persist the execution durably: a created execution must survive a restart of the Content Engine and remain retrievable. |
| FR-09 | The system shall report the outcome of every creation attempt to the requester: on success, the new execution's identifier and its recorded content; on rejection, every validation failure found (not only the first). |
| FR-10 | The system shall allow a created execution to be retrieved by its identifier, returning everything recorded at creation. |
| FR-11 | The system shall not, as any part of creation, start, build, launch, schedule, or otherwise act on the product or its tests. Creation must have no effect on the product's machine environment. |
| FR-12 | The system shall record executions immutably with respect to their creation facts: what was requested, by whom, and when must not be alterable after creation. (Whether *state* may later change — e.g. cancellation — is an Open Question; the creation facts themselves are fixed.) |
| FR-13 | The system shall permit multiple executions to exist for the same product, including with identical test scopes, distinguished by identifier and creation time. Discovery found a strict one-run-at-a-time constraint on the *machine*; that constrains running, not recording intent. |
| FR-14 | Where the requested test scope includes suite members that Discovery flagged as side-effectful beyond the test sandbox (the screenshot-regeneration test, which rewrites product documentation assets, and the report-export test, which writes into the operator's documents area), the system shall record that the scope includes such members so that later steps and reviewers can see it. Whether they are excluded by default is an Open Question, not a Step 1 rule. |

## 7. Non-Functional Requirements

| ID | Requirement |
|---|---|
| NFR-01 | **Traceability.** Every execution must be traceable to a registered product, a requester, and a point in time, with no anonymous or orphaned executions possible. |
| NFR-02 | **Durability.** No successfully reported creation may be lost; if the system cannot guarantee the record persisted, it must report failure, not success. |
| NFR-03 | **Determinism of validation.** The same request validated against the same product information must always produce the same accept/reject outcome and the same reasons. |
| NFR-04 | **Auditability.** Rejected attempts should be observable (at minimum in system records) so that repeated failing requests can be diagnosed; a rejection leaves no execution behind. |
| NFR-05 | **Clarity of feedback.** Rejection reasons must be expressed in terms the requester can act on (which input, what was wrong, what was expected), not internal fault language. |
| NFR-06 | **Responsiveness.** Creation is a bookkeeping act; it must complete promptly and must never block on, or wait for, the product's machine environment. |
| NFR-07 | **Name-drift resilience.** Because test names are the product's only test identifiers and Discovery proved they drift, the system's validation and records must make the inventory version/date they were checked against visible, so a later mismatch can be explained. |

## 8. User Inputs

Inputs the requester supplies to create an execution:

| Input | Required | Notes |
|---|---|---|
| Product reference | Yes | Must resolve to a registered product. (Which of the product's several names is the canonical reference is an Open Question inherited from Discovery.) |
| Test scope | Yes | Whole suite, named test class(es), or named individual test(s), per FR-03. |
| Purpose / description | Open Question | A short human statement of why this execution is being created (e.g. which release or content deliverable it serves). Discovery gives no basis to require or forbid it; flagged rather than invented. |
| Requester identity | Yes | May be supplied implicitly by the system's notion of the current user rather than typed; it must end up on the record either way. |

No other inputs are established by Discovery. In particular, Discovery found **no** category/tag/priority scheme in the product's test suite, so no "category" input exists to offer.

## 9. Expected Outputs

On **success**:
1. A new Workflow Execution exists, in the initial **Created** state.
2. The requester receives the execution's unique identifier.
3. The requester can see (immediately or on later retrieval) everything recorded: product reference and version information, test scope as validated, requester, creation timestamp, state, and any side-effect flags per FR-14.

On **rejection**:
1. No execution exists.
2. The requester receives the complete list of validation failures.

## 10. Validation Rules

| ID | Rule |
|---|---|
| VR-01 | The product reference must resolve to exactly one registered product. |
| VR-02 | The test scope must not be empty: it must name the whole suite or at least one class/test. |
| VR-03 | Every named class or test in the scope must exist in the product's test inventory. Matching is against the fully qualified names that Discovery established as the only machine-usable identifiers. |
| VR-04 | Duplicate entries within one scope (the same test named twice) must be detected; whether they are rejected or de-duplicated silently is an Open Question — but they must not produce a scope that means "run it twice", since no such semantics exist in the discovered execution mechanism. |
| VR-05 | All validation failures found are reported together (per FR-09). |
| VR-06 | Validation must not require, or attempt, contact with the product's machine environment beyond reading the test inventory the Content Engine already holds (see Precondition 2 and its Open Question on freshness). |

## 11. Business Rules

| ID | Rule |
|---|---|
| BR-01 | Creating an execution never runs anything. This is the defining rule of Step 1 and overrides any convenience ("create and start") behavior. |
| BR-02 | One execution references one product. Cross-product executions do not exist. |
| BR-03 | An execution, once created, belongs to the system's history: it can never be deleted in a way that erases the fact that it was created (see FR-12; later lifecycle states are future sprints' concern). |
| BR-04 | The execution records the product's version information *as known at creation time*, because Discovery showed the product's version is ambiguous mid-cycle; the record must therefore say what was known and when, not claim more precision than exists. |
| BR-05 | The Content Engine treats the product's test inventory as the sole authority on what tests exist — not prose documentation, which Discovery proved stale, and not previously recorded run evidence, which Discovery proved drifted. |
| BR-06 | Creation is permitted regardless of the state of any other execution (no "only one open execution" rule exists in any discovered constraint). Machine-level exclusivity is a rule about running, owned by a later step. |

## 12. Success Criteria

Step 1 is functionally successful when all of the following can be demonstrated:

1. A requester can create an execution for the registered product with each expressible scope granularity (whole suite; one class; one test), and each attempt yields a unique identifier and a retrievable record.
2. A creation attempt referencing an unregistered product is rejected with a reason, and leaves no record of an execution (though the attempt itself is observable per NFR-04).
3. A creation attempt naming a nonexistent test is rejected, and the rejection names the offending entry.
4. A created execution, retrieved after a system restart, shows exactly what was recorded at creation.
5. Creating an execution demonstrably causes no activity on the product side: nothing built, nothing launched, no files touched in the product's repository or machine environment.
6. Two executions with identical inputs can coexist and are distinguishable.

## 13. Failure Scenarios

Scenarios Step 1 must handle in a defined way (the required behavior stated with each):

| # | Scenario | Required behavior |
|---|---|---|
| F-01 | Product reference does not resolve | Reject; no execution; reason states the reference was not found among registered products. |
| F-02 | Test scope empty | Reject; reason states scope must name the suite, a class, or a test. |
| F-03 | Scope names a test/class absent from the inventory | Reject; reason names each missing entry (all of them, not the first). |
| F-04 | Scope mixes valid and invalid entries | Reject as a whole (partial creation of a "trimmed" scope is not permitted — the requester must get what they asked for or a clear refusal; silent narrowing would falsify intent). |
| F-05 | The system cannot persist the record | Report failure; no identifier is issued; the requester is not told "created". |
| F-06 | Requester identity unavailable | Reject; an anonymous execution violates NFR-01. |
| F-07 | Duplicate scope entries | Per VR-04: never results in double-run semantics; exact behavior pending the Open Question. |
| F-08 | The test inventory itself is unavailable or empty for the product | Reject with a reason distinguishing "your selection is wrong" from "the system has no inventory to check against" — these demand different corrective actions. |

## 14. Acceptance Criteria

Given/when/then statements a reviewer can test Step 1 against:

1. **Given** a registered product and a scope naming one existing test, **when** creation is requested, **then** an execution is created in the Created state, an identifier is returned, and the record shows product, version-as-known, scope, requester, and timestamp.
2. **Given** a registered product, **when** creation is requested with scope "whole suite", **then** the execution records that scope and additionally flags the known side-effectful suite members per FR-14.
3. **Given** an unknown product reference, **when** creation is requested, **then** the request is rejected, the reason identifies the unresolved reference, and no execution exists afterwards.
4. **Given** a scope naming two tests of which one does not exist, **when** creation is requested, **then** the whole request is rejected and the rejection names the nonexistent test.
5. **Given** a successfully created execution, **when** the Content Engine is restarted and the execution retrieved by its identifier, **then** the retrieved record equals the record reported at creation.
6. **Given** any successful creation, **when** the product's repository and machine environment are inspected, **then** nothing has changed there as a result of the creation.
7. **Given** two consecutive identical creation requests, **when** both succeed, **then** two executions exist with distinct identifiers and their own timestamps.
8. **Given** any rejected request, **then** the requester's feedback lists every failed validation, each in actionable terms (NFR-05).

## 15. Dependencies

| Dependency | Nature |
|---|---|
| Product registration capability (Step 1.2 discovery scope) | An execution references a registered product; registration must exist first and expose at least the identity facts catalogued in `Step03_ProductRegistration.md`. |
| Test inventory of the product (Step 1.1 discovery scope) | Validation (VR-03) needs the catalogued inventory; `Step02_TestCatalog.md` establishes what it contains and that runtime rediscovery is the only drift-proof source. |
| Requester identity | Some system notion of "who is asking" must exist (NFR-01, F-06). Its provider is unspecified here. |
| Resolution of blocking Open Questions | Items 1–3 in §17 must be answered before implementation can begin, since they determine validation behavior. |

Step 1 has **no** dependency on: the product being built, the product's machine being available, any test having ever been run, or any results-processing capability.

## 16. Assumptions

Only assumptions established as facts during Discovery are listed. Each carries its evidence.

| # | Assumption | Discovery evidence |
|---|---|---|
| A-01 | The product's UAT suite is addressable at three granularities — whole suite, test class, individual test — via fully qualified names. | Step01 §5–6; Step02 (filter mechanism is the only selection surface; FQNs are the only identifiers). |
| A-02 | Fully qualified test names are the only machine-usable test identifiers, and they are not guaranteed stable over time. | Step02 Open Questions #1, #11 (drift already observed between recorded evidence and source). |
| A-03 | No category/priority/ordering metadata exists in the product's test suite. | Step02 (zero trait/collection/orderer usage found). |
| A-04 | Every test of the product can be requested independently of any other (class-level isolation is guaranteed; within-class isolation is by convention). | Step02 dependencies summary. |
| A-05 | Two suite members have side effects outside the test sandbox (documentation screenshots; operator's documents area). | Step02 §16, §23; Step03 §8, RISKS #3. |
| A-06 | Running (not creating) is constrained to one run at a time per machine, on an interactive desktop, from a built repo checkout. | Step01 §6; Step03 RISKS #4–5. Recorded here only to justify FR-11/BR-06 boundaries. |
| A-07 | The product's version information can be ambiguous at any given time; a creation-time record can only capture "as known". | Step03 §12, RISKS #2, UNKNOWNS #8. |

## 17. Open Questions

Items that must be answered before or during implementation planning; none are answered by Discovery.

1. **Canonical product reference.** Which of the product's names (repository name, product string, assembly name, or a new Content Engine identifier) does a requester use? *(Inherited from Step03 UNKNOWNS #1.)*
2. **Inventory freshness at creation.** Is the test scope validated against the inventory captured at product registration, or must the inventory be re-discovered from the product at creation time? (Drift makes this materially important — a stale inventory can accept a scope that no longer exists, or reject one that now does.)
3. **Duplicate scope entries** (VR-04/F-07): reject, or silently de-duplicate?
4. **Are the unit-test suite, the screenshot-regeneration test, and the headless print-document tests selectable scopes**, or is the execution scope limited to the UI/UAT tests proper? *(Inherited from Step02 Open Questions #5–6; Step03 UNKNOWNS #5–6.)*
5. **Default handling of side-effectful members** in "whole suite" scope: included with a flag (the minimum FR-14 requires), excluded unless named explicitly, or requester's choice?
6. **Purpose/description input**: required, optional, or absent?
7. **Cancellation of a never-started execution**: is a "Created → Cancelled" transition part of the lifecycle, and if so, which sprint owns it?
8. **Multiple pending executions**: BR-06 permits unlimited coexisting Created executions. Is any business limit wanted (e.g. per product), or is unbounded accumulation acceptable?
9. **Who may create executions**: is every Content Engine user a valid requester, or is there a role restriction? (Actor enforcement was declared out of scope but the business answer is still needed.)
10. **Retention**: BR-03 forbids erasing history; is there nonetheless an archival horizon after which executions may be moved out of active view?
11. **Which product version does an execution claim to target** when the product's own version facts disagree (working-copy field vs. released build)? *(Inherited from Step03 UNKNOWNS #8.)*
