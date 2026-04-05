# Migration Parity Report

## Migration Project
- **Source**: [Library/Framework Name + Version]
- **Target**: [Library/Framework Name + Version]
- **Date**: [YYYY-MM-DD]
- **Migration Lead**: [Name]

---

## Executive Summary

| Metric | Value |
|:-------|:------|
| Total FIM Items | |
| Converted | |
| Verified (all tests pass) | |
| **Parity Score** | **____%** |
| Parity Gate | ✅ PASS / ❌ FAIL |

---

## 1. Cross-Reference Audit

### 1.1 Category Breakdown

| Category | Total | ✅ Done | 🔄 In Progress | ⬜ Pending | 🚫 Blocked |
|:---------|:------|:-------|:---------------|:----------|:----------|
| Features (F) | | | | | |
| Rules (R) | | | | | |
| Behaviors (B) | | | | | |
| Errors (E) | | | | | |
| Defaults (D) | | | | | |
| Config (C) | | | | | |
| **TOTAL** | | | | | |

### 1.2 Items NOT at ✅ Status

| ID | Category | Description | Current Status | Blocker | Remediation |
|----|----------|-------------|----------------|---------|-------------|
| | | | | | |

> ⚠️ **If ANY row exists in this table, the Parity Gate FAILS.**

---

## 2. Integration Verification

### 2.1 Feature Combination Tests

| Test Case | Features Combined | Source Result | Target Result | Match? |
|:----------|:-----------------|:-------------|:-------------|:------:|
| | | | | ✅/❌ |

### 2.2 End-to-End Data Flows

| Flow | Input | Source Output | Target Output | Match? |
|:-----|:------|:-------------|:-------------|:------:|
| | | | | ✅/❌ |

### 2.3 Error Propagation Chains

| Error Scenario | Source Behavior | Target Behavior | Match? |
|:---------------|:---------------|:---------------|:------:|
| | | | ✅/❌ |

---

## 3. Side-by-Side Comparison Results

### 3.1 Test Datasets Used

| Dataset | Size | Coverage | Purpose |
|:--------|:-----|:---------|:--------|
| | | | |

### 3.2 Comparison Results

| Dataset | Total Comparisons | Matches | Mismatches | Match Rate |
|:--------|:-----------------|:--------|:-----------|:-----------|
| | | | | % |

### 3.3 Mismatch Details

| # | Dataset | Input | Source Output | Target Output | Root Cause | Fix Applied |
|---|:--------|:------|:-------------|:-------------|:-----------|:------------|
| | | | | | | |

---

## 4. Items Requiring Adaptation

Items where the target platform cannot perfectly replicate source behavior:

| ID | Reason | Source Behavior | Target Adaptation | User Impact | Accepted? |
|----|:-------|:---------------|:------------------|:------------|:---------:|
| | | | | | ✅/❌ |

---

## 5. Known Behavioral Differences (Documented & Accepted)

Items where the target intentionally differs from source due to platform semantics:

| ID | Source Behavior | Target Behavior | Justification | Risk |
|----|:---------------|:----------------|:-------------|:-----|
| | | | Language semantics | None |

---

## 6. Quality Metrics

### 6.1 Test Coverage

| Metric | Value |
|:-------|:------|
| Total test cases | |
| Passing | |
| Failing | |
| Coverage % | |

### 6.2 Code Quality (Target)

| Metric | Value | Acceptable? |
|:-------|:------|:-----------|
| Lint warnings | | |
| Type errors | | |
| Code duplication | | |

---

## 7. Parity Gate Decision

### Gate Criteria

| Criteria | Required | Actual | Pass? |
|:---------|:---------|:-------|:-----:|
| All FIM items at ✅ | Yes | | ✅/❌ |
| Integration tests pass | Yes | | ✅/❌ |
| Side-by-side match ≥ 100% | Yes | | ✅/❌ |
| No unresolved mismatches | Yes | | ✅/❌ |
| All adaptations accepted | Yes | | ✅/❌ |

### Decision

- [ ] ✅ **PASS** — Proceed to Phase 5 (Optimization)
- [ ] ❌ **FAIL** — Return to Phase 3 with the following items:
  - [ ] [List specific items to fix]

---

## 8. Sign-Off

| Role | Name | Approval | Date |
|:-----|:-----|:---------|:-----|
| Migration Lead | | ✅/❌ | |
| Code Reviewer | | ✅/❌ | |
| Stakeholder | | ✅/❌ | |
