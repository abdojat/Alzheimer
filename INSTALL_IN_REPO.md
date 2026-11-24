# Installing the agent instructions

Extract this ZIP **into the root** of the local `Alzheimer` checkout, maintaining paths, so that the checkout contains:

```text
Alzheimer/
├── AGENTS.md
├── .github/
│   ├── copilot-instructions.md
│   └── instructions/
│       ├── unity-client.instructions.md
│       ├── server-rules.instructions.md
│       └── tests.instructions.md
└── docs/
    └── DEVELOPMENT_ROADMAP.md
```

Existing repo files are not intentionally replaced. The canonical rules remain at `docs/Alzheimer_Card_Game_Rules_v1.1.md`.

From PowerShell (adapt the ZIP path to the download location):

```powershell
cd D:\Users\abdal\Desktop\Alzheimer
Expand-Archive -Path "$env:USERPROFILE\Downloads\Alzheimer_Agent_Instructions.zip" -DestinationPath . -Force
git status --short
```

Open the **Alzheimer repository root** as the VS Code workspace, then use GitHub Copilot Chat in Agent mode. Review changes and commit when satisfied:

```powershell
git add AGENTS.md .github/copilot-instructions.md .github/instructions docs/DEVELOPMENT_ROADMAP.md
git commit -m "docs: add agent instructions and development roadmap"
```

IMPORTANT: The ZIP includes this `INSTALL_IN_REPO.md` as explanatory material. It is optional to commit and can be removed after installation. The agent files are guidance, not evidence that the code changes or tests have been completed.
