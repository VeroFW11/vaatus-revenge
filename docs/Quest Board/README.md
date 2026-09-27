# Quest Board

A gamified way to answer the open planning questions: **https://claude.ai/artifact/KY7uUinoK91mDWE5hzTAa1**

Each open question from the plan docs is a "trial" worth XP. Answering trials raises the team's rank (Unawakened → Avatar State), and each area fills up as you go: Air (story), Water (loop and rewards), Earth (builds and gear), Fire (combat and bosses), Forge (tools and team). Answering every question in all four element areas unlocks the Avatar State banner. There are also learning **side quests**, and an **idea scroll** for dropping notes from your phone.

## How it works

1. Open the board, choose **Playing as David / Jeremy**, and answer trials one at a time. Every answer has a notes box for extra context.
2. Answers are saved to the board's shared database, so both of you see them live.
3. When you've answered a batch, tell Claude: **"sync the quest board"**.

## Sync procedure (for Claude)

1. Read the board's `answers` and `ideas` collections with the `ArtifactData` tool (url above).
2. For each answered question, write the decision into the plan doc named in the question's `doc` field under **My notes**, and tick its checkbox under **Things to decide**. Respect the lore rules in `CLAUDE.md` and flag any conflicts.
3. File each idea from the scroll into the right plan doc, then set `filed: true` on that idea.
4. In the `bank/questions` document, set `status: "recorded"` on every synced question. Add new questions that came out of the answers, and keep `bank.json` in this folder in step with it.
5. Commit and push.

## Files

- `bank.json`: copy of the question bank (questions, side quests, ranks). The live copy is the board's `bank/questions` document.
- `quest-board.html`: the page source. Republish from the same session or pass the artifact URL to update it.
