# Mapperize examples

Each folder is a small, self-contained console app that demonstrates **one way to use Mapperize**
and doubles as a smoke test (it exits non-zero if the mapping is wrong). Run any of them with:

```bash
dotnet run --project samples/examples/<name>
```

| Example                                                              | Shows                                                                             |
| -------------------------------------------------------------------- | --------------------------------------------------------------------------------- |
| [`Example.Instance`](Example.Instance)                               | The classic instance mapper — rename, ignore, and automatic nested-object mapping. |
| [`Example.Static`](Example.Static)                                   | `static` mapping methods in a `static partial class` — no instance required.       |
| [`Example.Extension`](Example.Extension)                             | Extension methods (`this` on the source) for fluent `source.ToDto()` calls.        |
| [`Example.Update`](Example.Update)                                   | Mapping **into an existing instance** (`void`/fluent `Update`); init-only members are left alone. |
| [`Example.DependencyInjection`](Example.DependencyInjection)         | `services.AddMapperize()` plus a generated interface injected into a service.      |

For a single program that exercises every feature at once, see
[`../Mapperize.Sample`](../Mapperize.Sample).
