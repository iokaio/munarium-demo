# Customize the demo

Set `Demo__Name`, `Demo__LogoUrl`, and `Demo__ContactEmail` for your installation. The default mark is an original SVG. Serve a replacement logo from your own static directory and preserve any required image license. Configure your own verified email sender; branding does not configure mail delivery.

To add a corpus, define its input provenance and rights, a unique logical filename prefix, shapes, runbook collections, and access levels/compartments. Add its web configuration under `Corpora` and a matching Razor workspace. Update navigation, token-scope checks, the loader layout, data inventory, and acceptance cases together.

Provider IDs used by the current UI are `demo-anthropic`, `demo-openai`, `demo-openrouter`, and `demo-ollama`. Change installed models and provider tiers together, and check the catalog and readiness contracts. Unconfigured providers should not appear as usable choices. Model selection applies to query expansion and answer generation on the compatible Server release.

Keep expected answers separate from ingested sources. Test persona restrictions in both search and chat, including a denied case, before deploying a new collection policy.
