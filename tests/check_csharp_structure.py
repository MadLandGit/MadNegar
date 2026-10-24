#!/usr/bin/env python3
"""Basic lexical delimiter screening for C# sources. This is not a C# compiler."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
PAIRS = {')': '(', ']': '[', '}': '{'}
OPENERS = set(PAIRS.values())


def check_file(path: Path) -> list[str]:
    source = path.read_text(encoding='utf-8-sig')
    stack: list[tuple[str, int]] = []
    i = 0
    line = 1
    state = 'code'
    verbatim = False
    errors: list[str] = []
    while i < len(source):
        ch = source[i]
        nxt = source[i + 1] if i + 1 < len(source) else ''
        if ch == '\n':
            line += 1
        if state == 'line_comment':
            if ch == '\n':
                state = 'code'
            i += 1
            continue
        if state == 'block_comment':
            if ch == '*' and nxt == '/':
                state = 'code'
                i += 2
                continue
            i += 1
            continue
        if state in ('string', 'char'):
            if state == 'string' and verbatim and ch == '"' and nxt == '"':
                i += 2
                continue
            if ch == '\\' and not (state == 'string' and verbatim):
                i += 2
                continue
            if (state == 'string' and ch == '"') or (state == 'char' and ch == "'"):
                state = 'code'
                verbatim = False
            i += 1
            continue
        # code state
        if ch == '/' and nxt == '/':
            state = 'line_comment'
            i += 2
            continue
        if ch == '/' and nxt == '*':
            state = 'block_comment'
            i += 2
            continue
        if ch == '"':
            verbatim = i > 0 and source[i - 1] == '@'
            state = 'string'
            i += 1
            continue
        if ch == "'":
            state = 'char'
            i += 1
            continue
        if ch in OPENERS:
            stack.append((ch, line))
        elif ch in PAIRS:
            if not stack or stack[-1][0] != PAIRS[ch]:
                errors.append(f'{path.relative_to(ROOT)}:{line}: unmatched closing {ch}')
            else:
                stack.pop()
        i += 1
    if state == 'block_comment':
        errors.append(f'{path.relative_to(ROOT)}: unclosed block comment')
    elif state in ('string', 'char'):
        errors.append(f'{path.relative_to(ROOT)}: unclosed {state} literal')
    for token, token_line in stack:
        errors.append(f'{path.relative_to(ROOT)}:{token_line}: unclosed {token}')
    return errors


files = sorted(ROOT.rglob('*.cs'))
all_errors: list[str] = []
for file in files:
    all_errors.extend(check_file(file))
if all_errors:
    print('\n'.join(all_errors))
    sys.exit(1)
print(f'PASS: delimiter/comment/string screening for {len(files)} C# files (not a compiler)')
