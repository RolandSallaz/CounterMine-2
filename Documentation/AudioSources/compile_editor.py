from pathlib import Path
import re
p=Path('Library/Bee/artifacts/1900b0aE.dag/Assembly-CSharp-Editor.rsp');s=p.read_text(encoding='utf-8-sig')
s=re.sub(r'^-out:.*$', '-out:"Documentation/AudioSources/Validation/Assembly-CSharp-Editor.dll"',s,flags=re.M)
s=re.sub(r'^-refout:.*$', '-refout:"Documentation/AudioSources/Validation/Assembly-CSharp-Editor.ref.dll"',s,flags=re.M)
s=s.replace('Library/Bee/artifacts/1900b0aE.dag/Assembly-CSharp.ref.dll','Documentation/AudioSources/Validation/Assembly-CSharp.ref.dll')
s='\n'.join(line for line in s.splitlines() if not(line.startswith('"Assets/') and line.endswith('.cs"') and not Path(line.strip('"')).exists()))
for file in Path('Assets/Editor').glob('*.cs'):
 name=str(file).replace('\\','/')
 if '"'+name+'"' not in s:s+='\n"'+name+'"'
Path('Documentation/AudioSources/Validation/editor.rsp').write_text(s,encoding='utf-8')
