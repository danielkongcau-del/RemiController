# 一次性分析：从 codex 会话 JSONL 中提取读取/解算过的路径线索
# 用法: python parse_codex_session.py <session.jsonl>
import json, re, sys, collections

path = sys.argv[1]
# Windows 绝对路径：E:\ZZZ\... （在 JSON 字符串中反斜杠可能单写或双写）
winre = re.compile(r'[A-Za-z]:\\{1,2}(?:[^"\n\\]|\\{1,2}[^"\n\\]){1,}(?:\\{1,2}[^"\n\\]*)*')
nixre = re.compile(r'/[eE]/ZZZ/[^"\n\s;|)&`]+')

roots = collections.Counter()
samples = collections.defaultdict(set)
tools = collections.Counter()
commands = []


def norm(m):
    s = m.replace('\\\\', '\\')
    parts = s.replace('\\', '/').split('/')
    return '/'.join(parts[:6]).rstrip('/')


def classify(item_type, name):
    tools[(item_type, name)] += 1


def walk(x, ctx=''):
    if isinstance(x, str):
        if ctx == 'command' and len(commands) < 400:
            commands.append(x[:300])
        for m in winre.findall(x) + nixre.findall(x):
            n = norm(m)
            low = n.lower()
            if 'zzz' not in low:
                continue
            if any(skip in low for skip in ('/.codex/', '/users/catla', 'zcode/10_unity')):
                continue
            roots[n] += 1
            samples[n].add(m.replace('\\\\', '\\'))
    elif isinstance(x, dict):
        for k, v in x.items():
            walk(v, 'command' if k in ('command', 'arguments') else ctx)
    elif isinstance(x, list):
        for v in x:
            walk(v, ctx)


for line in open(path, encoding='utf-8'):
    try:
        o = json.loads(line)
    except Exception:
        continue
    payload = o.get('payload', o)
    if isinstance(payload, dict) and payload.get('name'):
        classify(o.get('type', ''), payload['name'])
    walk(o)

print('=== 工具/条目类型统计 ===')
for k, v in tools.most_common(12):
    print(f'{k[0]:16s} {k[1]:24s} {v}')
print()
print('=== 路径簇频次（按前 6 段归并，前 50）===')
for r, c in roots.most_common(50):
    print(f'{c:5d}  {r}')
print()
print('=== 每簇样例路径 ===')
for r, c in roots.most_common(50):
    ex = sorted(samples[r])[0]
    print(f'{r}\n     -> {ex[:150]}')
