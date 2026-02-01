# IntelligenceX Website Backlog

## Observed Issues (from review)
- Homepage FAQ shows raw `{{ for ... }}` / `{{ end }}`.
- Security page shows literal `**ChatGPT**` and `**your**` instead of bold.
- Link rules (external rel/target) apply to navigation but not Markdown body content.

## Root Cause Notes
- PowerForge does **not** execute Scriban inside Markdown. Only shortcodes are processed.
  The homepage embeds Scriban loops inside `content/pages/index.md` with `meta.raw_html: true`,
  so the loops render verbatim.
- Markdown emphasis and links are handled by the current renderer.
  It leaves inline Markdown untouched inside list items / definition-like lists,
  so `**bold**` and `[links](...)` show up literally.
  This is a renderer limitation, not a template issue.

## Proposed Fixes (Decision Needed)
1) Homepage FAQ rendering
   - Move the FAQ loop into a shortcode partial (e.g. `partials/shortcodes/faq.html`)
     and call it with `{{< faq data="faq" >}}` or use `meta.data_shortcode`.
   - Alternatively, move FAQ markup into a layout/partial and keep Markdown clean.
   - Engine option: enable `meta.content_engine: scriban` so Scriban loops can run in page content.

2) Markdown bold in list items
   - Engine fix: switch to a Markdown renderer that fully supports inline emphasis and links in lists.
   - Content workaround: replace `**...**` with `<strong>...</strong>` in affected lists.

3) Link handling in body content
   - Optional engine feature: apply LinkRules to rendered HTML (external rel/target, trailing slash).

## Next Steps (Recommended)
1) Replace homepage FAQ Scriban in Markdown with a shortcode or data override.
2) Decide on Markdown fix strategy (engine vs content workaround).
3) If desired, add LinkRules normalization for Markdown body links.
