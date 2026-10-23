# Prints a shell command with heredoc bodies and quoted strings emptied, so a commit message is not read
# as a command, then the body of each `sh -c '...'` / `eval "..."` on a line of its own, so a command
# hidden in one is still read as a command.
import re
import sys

QUOTED = r"'[^']*'|\"(?:[^\"\\]|\\.)*\""
EXEC_BODY = re.compile(r"(?:(?:^|[\s;&|({`])(?:(?:ba|z|da|k)?sh|eval|xargs)(?:\s+-\S+)*\s+)(" + QUOTED + ")", re.S)


def strip(text):
    text = re.sub(r"<<-?[ \t]*(['\"]?)(\w+)\1([^\n]*)\n.*?\n[ \t]*\2[ \t]*(?=\n|$)", r"<<\2\3", text, flags=re.S)
    bodies = [m.group(1)[1:-1] for m in EXEC_BODY.finditer(text)]
    text = re.sub(QUOTED, lambda m: m.group(0)[0] * 2, text)
    return text, bodies


def flatten(text, depth=0):
    text, bodies = strip(text)
    out = [text]
    if depth < 4:
        for body in bodies:
            out.extend(flatten(body, depth + 1))
    return out


sys.stdout.write("\n".join(flatten(sys.stdin.read())))
