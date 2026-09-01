# Project Context & Knowledge Base Integration

## Root Workspace
- Primary Project: `C:\UnityProjects\Cultivation Together`
- Tech Stack: Unity (C#), VContainer, MessagePipe, MCP Bridge (.NET 8), Luban
- Current Focus: UI Framework (MVP), Luban Data Migration, World Event System

## Integrated Knowledge Base (Karpathy's LLM Wiki)
- Vault Location: `C:\UnityProjects\Cultivation Together\LLMWiki`
- This folder contains my personal Obsidian vault with design docs, architecture notes, and research.
- **ALWAYS** treat `./LLMWiki/**/*.md` as authoritative reference material when answering questions about this project.
- When I ask about design decisions, lore, or architecture, search the LLMWiki folder FIRST before relying on general training data.
- Cross-reference code in the main project with notes in LLMWiki to ensure consistency.

## Behavioral Guidelines
1. If a question relates to project design/architecture, check `LLMWiki/` for relevant notes.
2. Cite specific wiki files when providing answers (e.g., "According to LLMWiki/architecture/mcp-bridge.md...").
3. The wiki is part of the workspace - you can read, search, and reference it directly without needing external Obsidian access.
4. Code lives in root; knowledge/design lives in `LLMWiki/`. Keep this separation clear.
5. Default language: Thai (unless asked otherwise). Mix English technical terms naturally.
