# Prints a shell command with heredoc bodies and quoted strings emptied, so a commit message is not read as a command.
import re
import sys

text = sys.stdin.read()
text = re.sub(r"<<-?[ \t]*(['\"]?)(\w+)\1([^\n]*)\n.*?\n[ \t]*\2[ \t]*(?=\n|$)", r"<<\2\3", text, flags=re.S)
text = re.sub(r"'[^']*'", "''", text)
text = re.sub(r'"(?:[^"\\]|\\.)*"', '""', text)
sys.stdout.write(text)
