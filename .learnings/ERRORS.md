## [ERR-20260414-001] powershell-start-process-redirect

**Logged**: 2026-04-14T00:00:00Z
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
PowerShell `Start-Process` 不能把 `RedirectStandardOutput` 和 `RedirectStandardError` 指向同一个文件。

### Error
```text
Start-Process : This command cannot be run because "RedirectStandardOutput" and "RedirectStandardError" are same.
```

### Context
- Command/operation attempted: 启动 Blazor 应用做 smoke test
- Input or parameters used: `Start-Process dotnet ... -RedirectStandardOutput smoke-test.log -RedirectStandardError smoke-test.log`
- Environment details if relevant: Windows PowerShell

### Suggested Fix
为标准输出和标准错误分别使用不同的日志文件，或者只重定向其中一个流。

### Metadata
- Reproducible: yes
- Related Files: smoke test command

### Resolution
- **Resolved**: 2026-04-14T00:00:00Z
- **Commit/PR**: pending
- **Notes**: 改为 `smoke-test.stdout.log` 和 `smoke-test.stderr.log` 两个独立日志文件后可正常启动并完成 smoke test。

---
