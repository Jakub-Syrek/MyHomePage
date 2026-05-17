# git-filter-repo --message-callback body. The script body is loaded
# verbatim and wrapped in a function whose input is `message` (bytes)
# and which must return bytes. Lives in its own file so the workflow
# YAML stays pure bash — no embedded heredoc / multi-line string that
# can confuse the workflow validator.

import re

text = message.decode("utf-8", errors="replace")

# Strip Co-Authored-By lines pointing at AI providers
text = re.sub(
    r"^Co-Authored-By:.*([Cc]laude|[Aa]nthropic|GPT|OpenAI|noreply@anthropic\.com).*\n?",
    "",
    text,
    flags=re.MULTILINE,
)

# Strip Claude Code / Generated-with markers and signatures
text = re.sub(r"\n?\U0001F916[^\n]*Generated[^\n]*\n?", "\n", text)
text = re.sub(r"\n?Generated with[^\n]*Claude[^\n]*\n?", "\n", text)
text = re.sub(r"\n?\[Claude Code\][^\n]*\n?", "\n", text)
text = re.sub(r"\n?https://claude\.com[^\s]*\n?", "\n", text)

# Collapse runs of blank lines and trim trailing whitespace
text = re.sub(r"\n{3,}", "\n\n", text).rstrip()
if not text.endswith("\n"):
    text += "\n"
return text.encode("utf-8")
