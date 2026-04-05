---
name: library-migration
description: "This skill should be used when migrating code from one library, framework, or language to another (e.g., OfficeJS to C# COM, jQuery to React, Python to Go). Enforces incremental migration with 100% functional parity verification through a systematic extract-convert-verify loop, preventing feature loss, rule omission, and incorrect translations."
category: engineering
risk: safe
source: custom
tags: "[migration, refactoring, library-replacement, parity-verification, code-conversion]"
date_added: "2026-03-28"
version: "2.0.0"
---

# Library Migration Skill

## Purpose

To provide a rigorous, systematic framework for migrating code between libraries, frameworks, or languages while guaranteeing **zero functional regression**. This skill prevents the most common migration failures: dropped features, lost business rules, incorrect API translations, and untested edge cases.

## When to Use This Skill

This skill should be used when:
- Replacing one library with another (e.g., OfficeJS → C# COM, Moment.js → Day.js)
- Converting code from one language to another (e.g., JavaScript → C#, Python → Go)
- Migrating between framework versions with breaking changes (e.g., Angular.js → Angular 17)
- Replacing a deprecated API with a modern alternative
- Consolidating multiple libraries into a single replacement

## Core Philosophy: The Three Laws of Migration

1. **LAW OF INVENTORY**: Every rule, feature, behavior, and edge case in the source MUST be cataloged before any code is written.
2. **LAW OF PARITY**: The target implementation MUST produce identical results to the source for ALL cataloged behaviors before proceeding to the next unit.
3. **LAW OF IDIOM**: Only AFTER 100% parity is proven may the target code be refactored to follow target-platform idioms and best practices.

## ⚠️ CRITICAL ANTI-PATTERNS TO AVOID

Before starting, internalize these failure modes:

| Anti-Pattern | What Happens | Prevention |
|:---|:---|:---|
| **Big Bang Migration** | Rewrite everything at once, nothing works | Use incremental unit-by-unit approach |
| **Copy-Paste Translation** | Blindly translating syntax without understanding semantics | Map APIs conceptually, not syntactically |
| **Skipping Inventory** | "I know what it does" → missing edge cases | Always create Feature Inventory Matrix |
| **Premature Optimization** | Refactoring during migration → losing parity | Phase 1 = parity only, Phase 2 = optimize |
| **Assumption-Driven Testing** | Testing only happy paths | Test edge cases, errors, boundaries explicitly |
| **Ignoring Platform Differences** | Assuming identical behavior across platforms | Research and document behavioral differences |
| **LLM Over-Reliance** | Accepting AI-generated code without verification | Always verify AI output against source behavior |
| **Skipping Golden Master** | No baseline captured → can't prove parity | Capture source output snapshots before writing any target code |

---

## Main Workflow

### Overview: The 8-Phase Migration Pipeline

```
Phase -1: DECISION      → Should we migrate at all? (Go/No-Go)
Phase 0:  DISCOVERY      → Understand source completely
Phase 1:  INVENTORY      → Catalog everything (features, rules, behaviors)
Phase 1.5: GOLDEN MASTER → Capture source behavior snapshots
Phase 2:  MAPPING        → Map source APIs to target APIs
Phase 3:  CONVERSION     → Convert unit-by-unit with verify loop
Phase 4:  PARITY GATE    → Full parity verification (100% match required)
Phase 5:  OPTIMIZATION   → Refactor to target-platform idioms
Phase 6:  CLEANUP        → Remove old code, update docs, retrospective
```

### ⚡ Session Continuity: Migration State Document

Migrations often span multiple work sessions. To prevent context loss:

**Create `MIGRATION_STATE.md`** in the project root and update it at the END of every session:

```markdown
## Migration State — [Date/Time]

### Current Phase: [Phase X]
### Last Completed Unit: [ID from FIM]
### Next Unit To Convert: [ID from FIM]
### Blockers: [any blockers]
### Decisions Made This Session:
- [decision 1 — rationale]
- [decision 2 — rationale]
### Open Questions:
- [question 1]
```

**Rules**:
- Update MIGRATION_STATE.md **before ending** every work session
- Include enough context for any engineer (or AI) to resume seamlessly
- Reference specific FIM IDs, not vague descriptions
- Log ALL decisions with rationale (prevents re-debating settled issues)
- This is the **first file to read** when resuming work

---

### Phase -1: Migration Decision (Go/No-Go)

**Goal**: Determine whether migration is the right choice before investing effort.

**Decision Matrix**:

| Approach | When to Use |
|:---------|:-----------|
| **Don't Migrate** | Code works, no security issues, meets requirements, low business impact |
| **Refactor** | Architecture is sound but code is messy/hard to test |
| **Replace** | Core functionality is commodity (auth, logging) — use off-the-shelf |
| **Migrate** | Technology is dead/deprecated, blocking new features, security risk |

**Go/No-Go Checklist**:
- [ ] Is there a clear business justification? (not just "newer = better")
- [ ] Is the source library truly blocked/deprecated/insecure?
- [ ] Do we have resources (time, expertise) for the full migration?
- [ ] Is the risk of regression acceptable?
- [ ] Have we estimated the effort? (see `references/effort-estimation.md`)

**If ANY answer is NO → Do NOT migrate. Document the decision and rationale.**

**Spike Test**: Before committing, perform a small-scale migration of ONE isolated module. This reveals hidden complexity and validates assumptions.

---

### Phase 0: Discovery & Source Analysis

**Goal**: Build complete understanding of the source implementation before touching any code.

**Actions**:
1. **Read the entire source codebase** — every file, every function, every comment
2. **Identify all entry points** — public APIs, event handlers, callbacks, exports
3. **Trace data flows** — input → processing → output for each feature
4. **Document dependencies** — what external libraries/services are called
5. **Find implicit behaviors** — error handling, defaults, type coercions, side effects
6. **Identify configuration** — environment variables, config files, feature flags
7. **Build Dependency Graph** — map which modules depend on which (needed for migration ordering)
8. **Assess existing test coverage** — identify what's tested vs. untested (risk zones)

**Dependency Graph & Migration Ordering**:
```
Module A ──depends on──→ Module B ──depends on──→ Module C (leaf)

Migration order: C first → B second → A last (leaf-first / bottom-up)
```
- Use topological sort: migrate leaf modules (fewest dependencies) first
- Break circular dependencies before migrating
- Prioritize: Leaf nodes → Shared utilities → Core logic → Entry points

**Output**: Source Analysis Document (see `references/source-analysis-template.md`)

---

### Phase 1: Feature Inventory Matrix (FIM)

**Goal**: Create an exhaustive, numbered catalog of every behavior in the source.

This is the **most critical phase**. Every missed item here becomes a bug later.

**Create the Feature Inventory Matrix**:

```markdown
| ID | Category | Feature/Rule | Source Location | Input Example | Expected Output | Edge Cases | Priority |
|----|----------|-------------|-----------------|---------------|-----------------|------------|----------|
| F001 | Formatting | Bold text conversion | format.js:45 | **text** | <b>text</b> | Nested bold, empty bold | P0 |
| F002 | Formatting | Italic text conversion | format.js:52 | *text* | <i>text</i> | Inside bold, at line start | P0 |
| R001 | Rule | Max cell length = 255 | validate.js:12 | 256 chars | Truncate + warning | Unicode, newlines in cell | P0 |
| B001 | Behavior | Auto-save on close | app.js:340 | Close event | Save then close | Unsaved + error state | P1 |
```

**Categories to catalog**:
- **F = Features**: What the code does (functions, transformations, outputs)
- **R = Rules**: Business logic constraints, validations, limits
- **B = Behaviors**: Side effects, event handling, lifecycle hooks
- **E = Error Handling**: How errors are caught, reported, recovered from
- **D = Defaults**: Default values, fallback behaviors, implicit configurations
- **C = Configuration**: Settings, options, parameters that modify behavior
- **S = Security**: Auth checks, input sanitization, permission gates

**Inventory Checklist** (must check ALL):
- [ ] All public methods/functions documented
- [ ] All private/internal helper functions documented
- [ ] All event handlers and callbacks documented
- [ ] All error handling paths documented
- [ ] All default values and fallbacks documented
- [ ] All input validation rules documented
- [ ] All output formatting rules documented
- [ ] All state management patterns documented
- [ ] All configuration options documented
- [ ] All edge cases documented (null, empty, overflow, unicode, concurrent)
- [ ] All security-sensitive operations documented (auth, sanitization, encryption)

---

### Phase 1.5: Golden Master Capture

**Goal**: Record the actual output of the source implementation as the "truth" baseline.

This phase creates **characterization tests** — tests that capture what the code *actually does*, not what we think it should do.

**Process**:
1. **Define test inputs** — cover happy paths, edge cases, error scenarios from FIM
2. **Run source code** with each input
3. **Record exact output** — save as snapshot files (the "Golden Master")
4. **Handle non-deterministic values** — mask timestamps, random IDs, environment-specific paths

**Golden Master Format**:
```markdown
| Test ID | Input | Source Output (Golden Master) | Category |
|---------|-------|-------------------------------|----------|
| GM-F001 | "**hello**" | "<b>hello</b>" | Feature |
| GM-R001 | "a" × 256 | "a" × 255 + "[TRUNCATED]" | Rule |
| GM-E001 | null | "Error: Input cannot be null" | Error |
```

**Why this matters**:
- Golden Master tests are the **primary verification mechanism** in Phase 3 and 4
- They prove parity objectively — no "I think it works" allowed
- They catch bugs in YOUR understanding of the source code
- If source code has bugs, the Golden Master captures them too (this is intentional — match first, fix later)

**⚠️ IMPORTANT**: Do NOT "fix" source bugs during capture. The goal is to match current behavior exactly. Source bugs are addressed AFTER achieving 100% parity.

---

### Phase 2: API Mapping Matrix

**Goal**: Map each source API/pattern to the correct target equivalent.

**CRITICAL**: Do NOT assume 1:1 mapping. Research each target API thoroughly.

```markdown
| Source API | Target API | Behavior Match? | Differences | Adaptation Needed |
|:-----------|:-----------|:---------------:|:------------|:------------------|
| `worksheet.getRange("A1")` | `worksheet.Cells[1,1]` | ⚠️ Partial | 0-indexed vs 1-indexed | Add index conversion helper |
| `range.setValue(val)` | `range.Value = val` | ✅ Full | Property vs method | Direct replacement |
| `Promise.all([...])` | `Task.WhenAll([...])` | ⚠️ Partial | Exception aggregation differs | Wrap with custom handler |
```

**For each mapping, verify**:
1. ✅ **Signature**: Do parameters match in type and order?
2. ✅ **Return type**: Is the return type equivalent?
3. ✅ **Side effects**: Does the target API have the same side effects?
4. ✅ **Error behavior**: Does the target throw the same errors?
5. ✅ **Threading/async model**: Is the concurrency model compatible?
6. ✅ **Null/empty handling**: Does the target handle null/empty the same way?
7. ✅ **Performance**: Are the performance characteristics acceptable?

**When no direct mapping exists**:
- Document the gap explicitly
- Design a wrapper/adapter function (Anti-Corruption Layer)
- Add to the "Adaptation Functions" list

**Anti-Corruption Layer (ACL)**:
When source and target have fundamentally different models, create an adapter layer:
```
Source Model → [ACL/Adapter] → Target Model
```
- ACL translates between source concepts and target concepts
- Keeps target code clean from source-specific workarounds
- Can be removed once migration is complete

---

### Phase 3: Unit-by-Unit Conversion (The Core Loop)

**Goal**: Convert one atomic unit at a time, verifying parity before moving on.

#### What is a "Unit"?
A unit is the **smallest independently testable piece of functionality**:
- A single function/method
- A single event handler
- A single validation rule
- A single data transformation

#### Migration Order
Follow the dependency graph from Phase 0:
1. **Leaf modules first** (no dependencies on other unmigrated code)
2. **Shared utilities** (used by multiple modules)
3. **Core business logic** (the heart of the application)
4. **Entry points last** (public APIs, event handlers)

#### The Convert-Verify Loop (CVL)

For EACH unit in the Feature Inventory Matrix:

```
┌─────────────────────────────────────────────────┐
│ STEP 1: SELECT next unconverted unit from FIM   │
│   (following dependency order)                  │
├─────────────────────────────────────────────────┤
│ STEP 2: WRITE target implementation             │
│   - Use API Mapping Matrix                      │
│   - Do NOT optimize, do NOT refactor            │
│   - Focus ONLY on functional equivalence        │
├─────────────────────────────────────────────────┤
│ STEP 3: VERIFY against Golden Master            │
│   - Run Golden Master test cases for this unit  │
│   - Compare target output vs Golden Master      │
│   - Check edge cases from FIM                   │
├─────────────────────────────────────────────────┤
│ STEP 4: EVALUATE result                         │
│   ├─ ✅ ALL tests pass → Mark unit DONE in FIM  │
│   │    → Go to STEP 1 for next unit             │
│   └─ ❌ ANY test fails → Go to STEP 5           │
├─────────────────────────────────────────────────┤
│ STEP 5: DEBUG & FIX                             │
│   - Identify exact discrepancy                  │
│   - Check: Is it a translation error or a       │
│     platform behavioral difference?             │
│   - Fix target implementation                   │
│   - Go back to STEP 3 (re-verify ALL tests)     │
│   ⚠️ Do NOT proceed until ALL tests pass        │
└─────────────────────────────────────────────────┘
```

#### AI-Assisted Conversion Guardrails

When using AI/LLM tools to assist with conversion:

| Risk | Mitigation |
|:-----|:-----------|
| **Hallucinated APIs** | Verify every API call against official documentation |
| **Subtle logic changes** | Always compare output against Golden Master, never trust "it looks right" |
| **Missing edge cases** | AI tends to handle happy paths well but miss edge cases — test boundaries explicitly |
| **Context window limits** | Break work into small units; never ask AI to convert an entire file at once |
| **Idiomatic bias** | AI may "improve" code during translation — reject any change that alters behavior |
| **Outdated API knowledge** | Verify AI suggestions against current (not training-data-era) API documentation |

**Rule**: Treat AI output as a **first draft**, not a finished product. Every line must be verified.

#### Conversion Rules

1. **One unit at a time** — never batch convert
2. **No optimization during conversion** — ugly but correct > elegant but wrong
3. **Never skip verification** — even for "trivial" conversions
4. **Document every decision** — especially when the target differs from source
5. **Test edge cases first** — they catch most translation errors
6. **Preserve error behavior** — same errors, same messages, same codes
7. **Verify against Golden Master** — not against your memory of what the source does

#### Progress Tracking

After each unit, update the tracking table:

```markdown
| ID | Status | Source | Target | Tests | Notes |
|----|--------|--------|--------|-------|-------|
| F001 | ✅ Done | format.js:45 | Format.cs:52 | 5/5 pass | Direct mapping |
| F002 | ✅ Done | format.js:52 | Format.cs:67 | 4/4 pass | Added null check |
| R001 | 🔄 In Progress | validate.js:12 | Validate.cs:? | 2/4 pass | Unicode edge case failing |
| B001 | ⬜ Pending | app.js:340 | — | — | — |
```

**Rollback Strategy**: If a unit proves impossible to convert correctly:
1. Document the blocker and root cause
2. Revert to last known-good state
3. Consider alternative approach (wrapper, adapter, or different API)
4. Never leave the codebase in a partially-converted, broken state

---

### Phase 4: Parity Gate (Full Verification)

**Goal**: Prove 100% functional parity between source and target.

**This phase is MANDATORY. Do not skip.**

#### 4.1 — Cross-Reference Audit

Go through the entire Feature Inventory Matrix:
- [ ] Every F (Feature) item has status ✅
- [ ] Every R (Rule) item has status ✅
- [ ] Every B (Behavior) item has status ✅
- [ ] Every E (Error Handling) item has status ✅
- [ ] Every D (Default) item has status ✅
- [ ] Every C (Configuration) item has status ✅
- [ ] Every S (Security) item has status ✅

If ANY item is not ✅, **go back to Phase 3**.

#### 4.2 — Integration Verification

Test features working together (not just in isolation):
- [ ] Feature combinations produce same results
- [ ] Data flows end-to-end match
- [ ] Error propagation chains work correctly
- [ ] State management across features is consistent

#### 4.3 — Golden Master Full Suite

Run the **complete** Golden Master test suite against the target:
- [ ] All Golden Master snapshots match
- [ ] No regressions from previously passing units
- [ ] Edge case suite passes 100%

#### 4.4 — Advanced Verification Techniques

**Property-Based Testing** (for complex logic):
- Define invariants that must hold regardless of input
- Generate hundreds of random inputs to find edge cases
- Example: "total account balance must be unchanged after migration"

**Contract Testing** (for service boundaries):
- Verify the target fulfills the same API contracts as the source
- Check that downstream consumers receive identical responses

#### 4.5 — Shadow Mode (Parallel Execution)

For high-risk migrations, run BOTH implementations side-by-side:

```
Input ──┬──→ Source Code → Source Output ─┐
        │                                  ├─→ COMPARE → Log differences
        └──→ Target Code → Target Output ─┘
                                          (target output discarded — source serves users)
```

**How it works**:
1. Both old and new code receive the same input
2. Source output is used as the "real" result
3. Target output is compared but discarded
4. Any differences are logged for investigation
5. Only when diff rate = 0% do you cut over to the target

**When to use Shadow Mode**:
- Critical business logic where failure is unacceptable
- Complex transformations where unit tests alone aren't sufficient
- High-volume data processing where edge cases appear only at scale

**⚠️ Caution**: Shadow mode doubles resource usage and must NOT allow the target to write/mutate shared state.

#### 4.6 — Non-Functional Requirements (NFR) Verification

Functional parity alone is not enough. Verify:

| NFR | How to Verify |
|:----|:-------------|
| **Performance** | Benchmark critical paths: latency (P95/P99), throughput (TPS) |
| **Memory** | Compare memory consumption under same workload |
| **Startup time** | Measure cold-start and warm-start times |
| **Error rates** | Compare error rates under stress |
| **Resource usage** | CPU, disk I/O, network — must not regress significantly |

⚠️ NFR regressions are acceptable ONLY if documented and explicitly approved.

#### 4.7 — Parity Report

Generate a final parity report (see `references/parity-report-template.md`):

```markdown
## Migration Parity Report

### Summary
- Total items in FIM: 47
- Converted: 47 (100%)
- Verified: 47 (100%)
- Golden Master match: 100% ✅
- Shadow Mode diff rate: 0% ✅ (if applicable)
- NFR benchmark: PASS ✅
- Parity score: 100% ✅
```

---

### Phase 5: Optimization & Idiomatic Refactoring

**Goal**: Now that parity is proven, refactor to follow target-platform best practices.

**ONLY enter this phase after Phase 4 Parity Gate passes at 100%.**

#### 5.1 — Code Quality Review

Apply target-platform idioms:
- [ ] Naming conventions (camelCase → PascalCase for C#, etc.)
- [ ] Design patterns native to the target platform
- [ ] Error handling patterns (try/catch → Result type, etc.)
- [ ] Async/await patterns appropriate to target
- [ ] Memory management patterns (GC, RAII, ownership, etc.)
- [ ] Dependency injection / IoC patterns
- [ ] Logging and observability patterns

#### 5.2 — Performance Optimization

- [ ] Replace naive translations with idiomatic performant alternatives
- [ ] Apply platform-specific optimizations (e.g., Span<T> in C#)
- [ ] Profile and benchmark critical paths
- [ ] Compare against Phase 4 NFR baseline

#### 5.3 — Re-verify After Each Optimization

**CRITICAL**: After EACH optimization change:
1. Re-run ALL Golden Master tests
2. Re-run ALL parity tests
3. If any test fails → revert the optimization
4. Only keep optimizations that maintain 100% parity

---

### Phase 6: Cleanup & Finalization

**Goal**: Remove old code, clean up infrastructure, update documentation.

#### 6.1 — Remove Old Code

- [ ] Delete source library/framework from dependencies
- [ ] Remove adapter/ACL layers (if no longer needed)
- [ ] Remove feature flags used during migration
- [ ] Remove shadow mode / parallel execution infrastructure
- [ ] Clean up Golden Master test files (or archive for reference)
- [ ] Delete `MIGRATION_STATE.md` (or archive)

#### 6.2 — Documentation Update

- [ ] Update README / architecture docs
- [ ] Update API documentation if interfaces changed
- [ ] Document any behavioral changes (even if intentional)
- [ ] Archive the Feature Inventory Matrix for future reference
- [ ] Archive the API Mapping Matrix for future reference

#### 6.3 — Migration Retrospective

Answer these questions and document in a `MIGRATION_RETROSPECTIVE.md`:
1. What took longer than expected? Why?
2. What edge cases did we miss in inventory? How?
3. What would we do differently next time?
4. What tooling would have helped?
5. How accurate was our initial effort estimation vs actual?
6. Which phase had the most surprises?

---

## Platform-Specific Considerations

See `references/common-gotchas.md` for comprehensive, detailed gotchas organized by migration direction. Summary:

### JavaScript → C# Common Gotchas
- `undefined` vs `null` — C# has no `undefined`
- Dynamic typing → static typing requires explicit casts
- Prototype chain → class inheritance model
- Event loop → Task-based async
- `==` loose equality → C# `==` is strict
- Array methods (map, filter, reduce) → LINQ equivalents (lazy evaluation!)
- Closures capture by reference in both, but scoping differs

### Python → Go Common Gotchas
- Dynamic typing → static typing
- Exceptions → error return values
- List comprehensions → explicit loops
- Duck typing → interfaces (structural typing)
- GIL → real concurrency (goroutines — race conditions are real)

### General Cross-Language Gotchas
- Date/time handling (timezone awareness, epoch formats — seconds vs milliseconds)
- String encoding (UTF-8 vs UTF-16, code points vs bytes)
- Number precision (float64 vs decimal vs BigInteger)
- Collection ordering guarantees (ordered dict vs HashMap)
- Null safety models (nullable reference types, Option/Maybe)
- Integer division behavior (floor vs truncation for negatives)
- Sort stability guarantees

---

## Bundled Resources

### references/
- `source-analysis-template.md` — Template for Phase 0 source analysis
- `feature-inventory-template.md` — Template for Phase 1 Feature Inventory Matrix
- `api-mapping-template.md` — Template for Phase 2 API mapping
- `parity-report-template.md` — Template for Phase 4 parity report
- `migration-checklist.md` — Complete migration checklist (all phases)
- `common-gotchas.md` — Platform-specific gotchas and solutions
- `effort-estimation.md` — Migration effort estimation guide

### Project Files (created during migration)
- `MIGRATION_STATE.md` — Session state document (update every session)
- `MIGRATION_RETROSPECTIVE.md` — Post-migration lessons learned

---

## Quick Reference: Migration Decision Tree

```
START: "I need to migrate from Library A to Library B"
  │
  ├─ Should I migrate at all?
  │   └─ UNCLEAR → Phase -1: Go/No-Go Decision
  │
  ├─ Have I read ALL source code?
  │   └─ NO → Phase 0: Discovery
  │
  ├─ Do I have a complete Feature Inventory?
  │   └─ NO → Phase 1: Inventory
  │
  ├─ Have I captured Golden Master snapshots?
  │   └─ NO → Phase 1.5: Golden Master Capture
  │
  ├─ Do I know the target API for each source API?
  │   └─ NO → Phase 2: Mapping
  │
  ├─ Have I converted and verified all units?
  │   └─ NO → Phase 3: Convert-Verify Loop
  │
  ├─ Have I passed the Parity Gate at 100%?
  │   └─ NO → Phase 4: Full Verification
  │
  ├─ Is the code idiomatic for the target platform?
  │   └─ NO → Phase 5: Optimization
  │
  └─ Is old code cleaned up and docs updated?
      └─ NO → Phase 6: Cleanup
      └─ YES → ✅ MIGRATION COMPLETE
```
