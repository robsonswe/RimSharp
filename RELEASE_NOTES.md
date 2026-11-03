### 💡 Highlights
Patch v2.4.4 - contrast and text styling refinements across dialogs and panels (dark text on light panels, button hover states, header and link colors), Steam BBCode text-color fix, Git mod badge color, build-warning cleanup (logger null-conditional, PlaceholderText, null-safe description parsing), Replacements modal uses Steam title/author via profile XML with CDATA fix, Duplicates/Replacements dialogs prefer DisplayName, ModReplacementService JSON fallback maps names and versions; VramAnalysis deterministic baseline fix (VM constructor race)

### 🚀 What's Changed (since v2.4.3)
641a525 fix(vram): deterministic baseline calculation; replaced with vram fix + CI build-and-test workflow
ed234d6 Give Git mod badges a distinct color
8fb2e37 Fix build warnings
f5441b8 Refine text contrast styling
e57db5a Refactor application styling utilities
a68c450 fix(dialogs): replacements modal uses Steam title/author via API + profile XML CDATA fix; duplicates dialog prefers DisplayName; JSON fallback maps names/versions
