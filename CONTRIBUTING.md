# Contributing to BusWorks

## Commit Message Convention

BusWorks uses structured commit messages to drive **automatic semantic versioning**.
Every push to `main` or a `feature/*` branch produces a NuGet package — the version
is determined entirely by the commit messages since the last release.

---

> ### ⚠️ Squash merge is used on all PRs
> When you squash merge, the **PR title becomes the single commit** on the target branch.
> That title is the only thing GitVersion reads to determine the version bump.
>
> **Your PR title must follow the convention below.**
> Individual commit messages inside the PR branch don't affect the version.

---

### Format

```
<type>(<optional scope>): <short description>
```

Examples of valid PR titles:
```
feat: add session-based FIFO consumer support
fix(consumer): null reference when message body is empty
breaking: rename IEventBusPublisher to IEventPublisher
chore: bump Azure.Messaging.ServiceBus to 7.20.1
```

### Types and their version impact

| Type | Version bump | When to use |
|---|---|---|
| `breaking` | **major** `2.0.0` | Public API changes that require consumers to update their code |
| `feat` | **minor** `1.1.0` | New public API that is fully backwards compatible |
| `fix` | **patch** `1.0.1` | Bug fix that does not change any public contracts |
| `perf` | **patch** `1.0.1` | Performance improvement with no behaviour change |
| `refactor` | none | Internal restructure — no public API or behaviour change |
| `chore` | none | Dependency bumps, tooling, configuration |
| `docs` | none | Documentation only |
| `test` | none | Adding or updating tests |
| `ci` | none | Pipeline or build system changes |
| `build` | none | Project/SDK changes that don't affect the package |

> Commits that don't match any prefix fall back to a **patch** bump.

---

### When to use each bump

#### `breaking:` — major
The consumer's code **will not compile or behave the same** after upgrading.

```
breaking: rename IEventBusPublisher to IEventPublisher
breaking(consumer): change IConsumer<T> method signature
```

Use this when you:
- Rename or remove a public type, method, or property
- Change a method signature or return type
- Change behaviour that consumers depend on

#### `feat:` — minor
New capability that consumers can **optionally adopt** — nothing existing breaks.

```
feat: add session-based FIFO consumer support
feat(publisher): add support for message scheduling
```

Use this when you:
- Add a new public interface, class, or method
- Add a new optional configuration property
- Add support for a new Azure Service Bus feature

#### `fix:` / `perf:` — patch
Safe to upgrade — fixes a bug or improves performance, no contract change.

```
fix: null reference in consumer when message body is empty
fix(dead-letter): messages not being forwarded after max delivery count
perf: reduce allocations in message dispatcher hot path
```

#### No bump (`refactor`, `chore`, `docs`, `test`, `ci`, `build`)
Internal changes only — no package is changed in a meaningful way.

```
refactor: extract MessageDispatcher from background service
chore: bump Azure.Messaging.ServiceBus to 7.20.1
docs: add queue routing example to README
test: add integration tests for session consumer
ci: add feature branch trigger to pipeline
```

---

### Scopes (optional)

A scope narrows the context of the change. Use the component name:

```
feat(publisher): add fire-and-forget overload
fix(consumer): handle null correlation id
breaking(session): rename ISessionedEvent to ISessionEvent
```

---

### Branch and publish behaviour

| Branch | Package version | Published to |
|---|---|---|
| `feature/*` push | `1.1.0-alpha.3` | Internal Azure Artifacts feed |
| `main` push | `1.1.0` | Internal feed **+** NuGet.org |
| Pull Request | *(calculated, not published)* | Nowhere — CI validation only |

Version numbers are calculated automatically by
[GitVersion](https://gitversion.net) from your commit history.
You never set a version manually.

