# Complete Migration Checklist

Use this as a master checklist to track progress across all phases.

---

## Phase -1: Migration Decision (Go/No-Go)
- [ ] Clear business justification exists
- [ ] Source library is truly blocked/deprecated/insecure
- [ ] Resources (time, expertise) available for full migration
- [ ] Risk of regression assessed and acceptable
- [ ] Effort estimated using estimation guide
- [ ] Pilot migration completed on one small module
- [ ] **GO / NO-GO decision documented**

---

## Phase 0: Discovery & Source Analysis
- [ ] Read ALL source code files
- [ ] Document file structure with line counts
- [ ] List all entry points (public APIs, exports, handlers)
- [ ] Trace all data flows (input → processing → output)
- [ ] Document all external dependencies and versions
- [ ] Identify implicit behaviors (defaults, coercions, side effects)
- [ ] Document all configuration and environment variables
- [ ] Build dependency graph (module → module)
- [ ] Determine migration order (topological sort, leaf-first)
- [ ] Identify complexity hotspots and platform-specific code
- [ ] Assess existing test coverage (identify risk zones)
- [ ] Complete Source Analysis Document

---

## Phase 1: Feature Inventory Matrix
- [ ] Catalog all Features (F) — what the code does
- [ ] Catalog all Rules (R) — business constraints, validations
- [ ] Catalog all Behaviors (B) — side effects, lifecycle
- [ ] Catalog all Error Handling (E) — catch, report, recover
- [ ] Catalog all Defaults (D) — fallback values, implicit config
- [ ] Catalog all Configuration (C) — settings, params
- [ ] Catalog all Security (S) — auth, sanitization, permissions
- [ ] Every item has concrete input/output example
- [ ] Every item has identified edge cases
- [ ] Every item has priority (P0-P3)
- [ ] Completeness checklist passes (all sub-items)
- [ ] Feature Inventory Matrix document complete

---

## Phase 1.5: Golden Master Capture
- [ ] Define test inputs covering happy paths, edge cases, errors
- [ ] Run source code with each input
- [ ] Record exact output as snapshot files
- [ ] Handle non-deterministic values (mask timestamps, IDs)
- [ ] Golden Master test suite is automated and repeatable
- [ ] All FIM items have at least one Golden Master test case

---

## Phase 2: API Mapping Matrix
- [ ] Map every source API to target equivalent
- [ ] Mark match level for each (Full/Partial/None/N/A)
- [ ] Document behavioral differences for partial matches
- [ ] Design adapter/ACL functions for "no match" items
- [ ] Verify each mapping with 7-point checklist:
  - [ ] Signature match
  - [ ] Return type match
  - [ ] Side effects match
  - [ ] Error behavior match
  - [ ] Null/empty handling match
  - [ ] Threading/async model match
  - [ ] Performance characteristics acceptable
- [ ] Document all platform behavioral differences
- [ ] API Mapping Matrix document complete

---

## Phase 3: Unit-by-Unit Conversion

### For EACH unit (repeat until all done):
- [ ] Select next unconverted unit (following dependency order)
- [ ] Write target implementation using API Mapping Matrix
- [ ] Verify against Golden Master test cases
- [ ] Compare target output vs Golden Master snapshots
- [ ] Check all edge cases from FIM
- [ ] If tests fail: debug → fix → re-verify ALL tests
- [ ] Mark unit as ✅ Done in FIM only when ALL tests pass
- [ ] Update progress tracking table

### AI-Assisted Conversion (if using AI tools):
- [ ] Every AI-suggested API verified against official docs
- [ ] Output compared against Golden Master (not just "looks right")
- [ ] Edge cases explicitly tested (AI tends to miss these)
- [ ] No behavioral changes accepted — only functional equivalence
- [ ] Work broken into small units (not full files)

### Conversion Quality Gates:
- [ ] No premature optimization applied
- [ ] No untested units proceeding
- [ ] Every decision documented
- [ ] Error behaviors preserved
- [ ] Edge cases tested first
- [ ] Rollback possible for each unit

---

## Phase 4: Parity Gate

### 4.1 Cross-Reference Audit
- [ ] Every F (Feature) item has status ✅
- [ ] Every R (Rule) item has status ✅
- [ ] Every B (Behavior) item has status ✅
- [ ] Every E (Error Handling) item has status ✅
- [ ] Every D (Default) item has status ✅
- [ ] Every C (Configuration) item has status ✅
- [ ] Every S (Security) item has status ✅

### 4.2 Integration Verification
- [ ] Feature combination tests pass
- [ ] End-to-end data flow tests pass
- [ ] Error propagation chain tests pass
- [ ] State management consistency verified

### 4.3 Golden Master Full Suite
- [ ] Complete Golden Master suite passes against target
- [ ] No regressions from previously passing units
- [ ] Edge case suite passes 100%

### 4.4 Advanced Verification (if applicable)
- [ ] Property-based tests defined and passing
- [ ] Contract tests verified (for service boundaries)

### 4.5 Shadow Mode (if applicable)
- [ ] Shadow mode infrastructure set up
- [ ] Both old and new code receive identical inputs
- [ ] Outputs compared automatically
- [ ] Differences logged and investigated
- [ ] Diff rate reaches 0% before cutover
- [ ] Shadow mode does NOT write/mutate shared state

### 4.6 Non-Functional Requirements
- [ ] Performance benchmarked (latency P95/P99, throughput)
- [ ] Memory consumption compared
- [ ] Startup time measured
- [ ] Error rates under stress verified
- [ ] Resource usage (CPU, disk, network) acceptable

### 4.7 Parity Report
- [ ] Parity Report document completed
- [ ] All Gate Criteria evaluated
- [ ] Adaptations documented and accepted
- [ ] Known behavioral differences justified
- [ ] **PARITY SCORE = 100%**

---

## Phase 5: Optimization & Idiom Refactoring

### 5.1 Code Quality
- [ ] Naming conventions follow target platform standards
- [ ] Design patterns are native to target platform
- [ ] Error handling uses target-platform idioms
- [ ] Async/await patterns are correct for target
- [ ] Memory management follows target best practices
- [ ] Dependency injection patterns applied
- [ ] Logging and observability added

### 5.2 Performance
- [ ] Naive translations replaced with idiomatic alternatives
- [ ] Platform-specific optimizations applied
- [ ] Critical paths profiled and benchmarked
- [ ] Performance compared against Phase 4 NFR baseline

### 5.3 Re-verification
- [ ] ALL Golden Master tests re-run after EACH optimization
- [ ] ALL parity tests re-run after EACH optimization
- [ ] No test regressions from optimizations
- [ ] Final parity score still 100%

---

## Phase 6: Cleanup & Finalization

### 6.1 Remove Old Code
- [ ] Source library removed from dependencies
- [ ] Adapter/ACL layers removed (if no longer needed)
- [ ] Feature flags used during migration removed
- [ ] Shadow mode infrastructure removed
- [ ] Golden Master files archived or cleaned up
- [ ] `MIGRATION_STATE.md` archived or deleted

### 6.2 Documentation
- [ ] README / architecture docs updated
- [ ] API documentation updated
- [ ] Behavioral changes documented
- [ ] Feature Inventory Matrix archived
- [ ] API Mapping Matrix archived

### 6.3 Retrospective
- [ ] `MIGRATION_RETROSPECTIVE.md` written
- [ ] Effort estimation accuracy reviewed
- [ ] Lessons learned documented
- [ ] Team notified of migration completion

---

## Session Continuity

### Before Ending EVERY Work Session:
- [ ] Update `MIGRATION_STATE.md` with:
  - [ ] Current phase
  - [ ] Last completed unit (FIM ID)
  - [ ] Next unit to convert (FIM ID)
  - [ ] Any blockers
  - [ ] Decisions made this session (with rationale)
  - [ ] Open questions

