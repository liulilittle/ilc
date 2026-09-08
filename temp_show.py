import pathlib
bak=r'E:\il2cpp\.opencode\backups\CodeGenerator.cs.20260902_140357_l2migrate'
cur=r'E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs'
def show(path, start, end):
    lines=open(path, encoding='utf-8', errors='ignore').readlines()
    print(f'--- {path} lines {start}-{end} total {len(lines)} ---')
    for i in range(start-1, min(end, len(lines))):
        print(f'{i+1:4d}: {lines[i].rstrip()[:200]}')
for rng in [(1900,2040),(2051,2128),(4054,4113),(6540,6560)]:
    show(bak, rng[0], rng[1])
    print()
    show(cur, rng[0], rng[1])
    print('='*80)
