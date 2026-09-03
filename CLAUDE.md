# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.

---

# NX Open 事实源（本仓库的 NX 工作专用规则）

本仓库围绕 NX 2406 插件设计/实现（设计文档：`docs/nxopen-research.md`、`docs/nx-plugin-design.md`）。
凡任务涉及 **NXOpen API（类型/成员/枚举/参数取值形态/模板名/许可）**，无论写文档还是写代码，先遵守：

1. **先查索引，以索引为准**：`docs/nx2406-install-index.md` 是本机 NX2406 安装资料的
   资源索引 + API 事实速查（对象模型 / 属性取值四形态 / 枚举宿主 / 版本许可注记 /
   "不存在项"清单）。两篇设计文档中的 API 细节若有出入，以索引（及 nxopen-research 附 A）为准。
2. **实证优先，禁止凭记忆写 API**：索引/附 A 未覆盖、或与旧文献冲突、或需要精确签名/枚举值的
   成员 → **自动调用 `/nx-api-verify` skill**（`.claude/skills/nx-api-verify/`，内含三路查证协议与
   现成命令/脚本：① `NXBIN\managed\NXOpen.xml` 成员清单与 remarks ② PowerShell 反射 `NXOpen.dll`
   ③ `UGOPEN\NXOpen\*.hxx` C++ 声明；不确定时直接运行，不要自行凭记忆拼 API 名）；
   核实后再写代码，并把结论回填索引/附 A（含"不存在项"与"待运行时验证"归位）。
3. **待运行时验证项不得固化**：索引 §3 清单（typeName 字面量、Stepover 链路、
   `CreateCamSetup` 模板名、`run_journal.exe -nogui` 等）未在 NX 会话/批处理实测前，
   代码与文档只可标注"待实测"，不得当作最终接口使用。
4. 改动上述 NX 文档或本规则引用的内容时，保持与索引一致；发现索引过期立即修正。
