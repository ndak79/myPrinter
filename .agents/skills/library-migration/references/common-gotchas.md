# Common Migration Gotchas & Solutions

A comprehensive reference of platform-specific pitfalls organized by migration direction.

---

## JavaScript → C# (.NET)

### Type System
| JS Pattern | C# Equivalent | ⚠️ Gotcha |
|:-----------|:-------------|:----------|
| `undefined` | No equivalent | Use `null` or `default`. Distinguish "not set" vs "empty" with `Nullable<T>` |
| `null == undefined` → `true` | `null == null` → `true` | JS loose equality has no C# parallel. Always use strict checks |
| Dynamic typing | Static typing | Every variable needs an explicit type. Use `var` for inference, `dynamic` for truly dynamic |
| `typeof x` | `x.GetType()` / `is` | JS `typeof null === "object"` is a famous bug. C# is correct |
| Truthy/falsy (`""`, `0`, `null`) | Explicit bool checks | C# has no truthy/falsy. Must check each condition explicitly |

### Async Patterns
| JS Pattern | C# Equivalent | ⚠️ Gotcha |
|:-----------|:-------------|:----------|
| `async/await` | `async/await` | Similar syntax but different error handling. C# wraps in `AggregateException` |
| `Promise.all([...])` | `Task.WhenAll([...])` | C# throws first exception only by default. Wrap to collect all |
| `Promise.race([...])` | `Task.WhenAny([...])` | C# returns the completed Task, not the result directly |
| `setTimeout(fn, ms)` | `Task.Delay(ms)` | No direct callback model. Use `await Task.Delay()` + continuation |
| Event loop (single-threaded) | Thread pool (multi-threaded) | C# tasks may run on different threads. Watch for thread-safety |

### Collections
| JS Pattern | C# Equivalent | ⚠️ Gotcha |
|:-----------|:-------------|:----------|
| `array.map(fn)` | `list.Select(fn)` | LINQ is lazy! Call `.ToList()` or `.ToArray()` to materialize |
| `array.filter(fn)` | `list.Where(fn)` | Also lazy. Materialize before modifying source collection |
| `array.reduce(fn, init)` | `list.Aggregate(init, fn)` | Parameter order differs |
| `array.find(fn)` | `list.FirstOrDefault(fn)` | Returns `default(T)` not `undefined` when not found |
| `Object.keys(obj)` | Dictionary keys | JS object property order is insertion order (mostly). C# `Dictionary` has no guaranteed order |
| `[...arr]` spread | `new List<T>(arr)` | No spread operator in C#. Use constructor, `Concat()`, or `AddRange()` |

### String & Regex
| JS Pattern | C# Equivalent | ⚠️ Gotcha |
|:-----------|:-------------|:----------|
| Template literals `` `${var}` `` | `$"interpolated {var}"` | Nearly identical, but C# requires `$` prefix |
| `str.match(regex)` | `Regex.Match(str)` | Different return types. C# returns `Match` object |
| `str.replace(/pat/g, rep)` | `Regex.Replace(str, pat, rep)` | JS needs `g` flag for global. C# replaces all by default |
| `/regex/i` | `RegexOptions.IgnoreCase` | Flags are enum values, not inline modifiers |

### Error Handling
| JS Pattern | C# Equivalent | ⚠️ Gotcha |
|:-----------|:-------------|:----------|
| `try/catch(e)` | `try/catch(Exception e)` | C# catch is typed. Catch specific exceptions first |
| `throw new Error("msg")` | `throw new Exception("msg")` | C# has rich exception hierarchy. Use specific types |
| `finally` | `finally` | Same semantics. C# also has `using` for disposables |
| `catch` catches everything | `catch(Exception)` catches managed | C# can also have unmanaged exceptions |

---

## Python → Go

### Type System
| Python | Go | ⚠️ Gotcha |
|:-------|:---|:----------|
| Dynamic typing | Static typing | Every variable needs explicit type |
| `None` | Zero values (`nil`, `""`, `0`) | Go has no universal null. Each type has its zero value |
| Duck typing | Interfaces (structural) | Go interfaces are implicitly satisfied — no `implements` keyword |
| `isinstance(x, Type)` | Type assertion `x.(Type)` | Go type assertions can panic. Use comma-ok pattern |

### Error Handling
| Python | Go | ⚠️ Gotcha |
|:-------|:---|:----------|
| `try/except` | `if err != nil` | Go has no exceptions. EVERY function can return an error |
| `raise ValueError("msg")` | `return fmt.Errorf("msg")` | Errors are values, not control flow |
| `except Exception as e:` | `if err != nil { ... }` | Must check error at every call site |
| Stack trace in exceptions | No automatic stack trace | Use `pkg/errors` or `runtime.Stack()` for traces |

### Concurrency
| Python | Go | ⚠️ Gotcha |
|:-------|:---|:----------|
| `threading` (GIL-limited) | Goroutines (real parallelism) | Go goroutines run truly concurrently. Race conditions are real |
| `asyncio` | Goroutines + channels | Different model entirely. Go uses CSP, Python uses event loop |
| `multiprocessing` | Goroutines | Go goroutines are lightweight. No process overhead |
| `Lock()` | `sync.Mutex` | Same concept, different API |

---

## Python → TypeScript/JavaScript

### Type System
| Python | TypeScript | ⚠️ Gotcha |
|:-------|:-----------|:----------|
| `Optional[str]` | `string \| undefined` | TS distinguishes `null` and `undefined` |
| `List[int]` | `number[]` | TS has no int/float distinction. All numbers are `number` |
| `Dict[str, Any]` | `Record<string, any>` | Similar but TS has stricter index signatures |
| `Tuple[int, str]` | `[number, string]` | TS tuples are arrays with fixed types per position |

---

## General Cross-Language Gotchas

### Date/Time
| Concern | Common Pitfall | Solution |
|:--------|:-------------|:---------|
| Timezone awareness | Source uses UTC, target uses local | Always explicitly set timezone. Never rely on defaults |
| Epoch format | Seconds vs milliseconds | JS uses milliseconds, Unix uses seconds. Always verify |
| Date parsing | Different format strings | Research target's date format syntax. They ALL differ |
| Daylight saving | DST transitions cause hour skips | Use timezone-aware types. Test with DST transition dates |

### String Encoding
| Concern | Common Pitfall | Solution |
|:--------|:-------------|:---------|
| UTF-8 vs UTF-16 | Length differences | JS/Java use UTF-16 internally. Python 3 uses Unicode code points. C# uses UTF-16 |
| String length | Emoji/surrogate pairs | `"😀".length` is 2 in JS, 1 in Python, 2 in C# | Use grapheme cluster counting if needed |
| Byte vs character | Slicing by bytes vs chars | Always be explicit about byte operations vs character operations |

### Numeric Precision
| Concern | Common Pitfall | Solution |
|:--------|:-------------|:---------|
| Float precision | `0.1 + 0.2 !== 0.3` | Use Decimal/BigDecimal for financial calculations |
| Integer overflow | Silent overflow vs exception | JS has no integer overflow (BigInt available). C# throws by default in checked context |
| Division | Integer division rules differ | Python 3: `//` is floor div. C#: `/` is truncation div for ints. These differ for negatives! |

### Collection Ordering
| Concern | Common Pitfall | Solution |
|:--------|:-------------|:---------|
| Dictionary order | Some preserve insertion order, some don't | Python 3.7+ `dict` preserves order. C# `Dictionary<>` doesn't guarantee it. Use `OrderedDictionary` |
| Sort stability | Some sorts are stable, some aren't | Verify sort stability guarantees for your target platform |
| Set iteration | No guaranteed order | Never depend on set iteration order in any language |

### Null Safety
| Language | Null Model | Key Differences |
|:---------|:-----------|:---------------|
| JavaScript | `null` + `undefined` | Two distinct "nothing" values. `==` equates them, `===` doesn't |
| TypeScript | `null` + `undefined` + strict null checks | Enable `strictNullChecks` to catch issues at compile time |
| Python | `None` | Single null value. Use `is None` not `== None` |
| C# | `null` + nullable reference types | Enable nullable reference types for compile-time safety |
| Go | Zero values + `nil` (for pointers, slices, maps, interfaces) | No universal null. Must check nil for each reference type |
| Rust | `Option<T>` | No null at all. Must handle `Some`/`None` explicitly |
| Kotlin | `Type?` nullable types | Null safety built into type system |

---

## Migration-Specific Pitfalls

### The "It Works in the Source" Trap
**Problem**: Assuming that because something works in the source language, the same approach will work in the target.

**Example**: 
```javascript
// JS: This works because of implicit type coercion
if (value) { doSomething(); }  // "" and 0 are falsy
```
```csharp
// C#: This does NOT work the same way
if (value) { DoSomething(); }  // Compile error — value must be bool
```

**Prevention**: Test EVERY conditional/comparison with the actual values from edge cases.

### The "Close Enough" Trap
**Problem**: Two APIs look similar but behave differently in edge cases.

**Example**:
```javascript
// JS: Array.prototype.sort modifies in-place AND returns the array
const sorted = arr.sort();  // arr is ALSO sorted
```
```csharp
// C#: LINQ OrderBy returns a new sequence, does NOT modify original
var sorted = list.OrderBy(x => x);  // list is NOT modified
```

**Prevention**: Always verify with the 7-point API verification checklist from the API Mapping Matrix.

### The "Lost Default" Trap
**Problem**: Source library has default behaviors that are invisible until they're missing.

**Example**: A JSON parser that defaults to case-insensitive key matching. The replacement defaults to case-sensitive. All existing data with mixed-case keys suddenly fails.

**Prevention**: Phase 1 Inventory must catalog ALL defaults (D-category items), even "obvious" ones.
