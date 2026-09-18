from pathlib import Path
import urllib.request,re,concurrent.futures,hashlib,json
folder=Path('Documentation/AudioSources')
def get(k):
 s=(folder/(k+'.html')).read_text(encoding='utf8'); urls=[u for u in re.findall(r'href=[\"\']([^\"\']+)',s) if re.search(r'\.(zip|7z|wav)$',u)]
 if k=='reload': urls=[u for u in urls if 'assaultrifle' in u]
 result=[]
 for u in dict.fromkeys(urls):
  p=folder/urllib.parse.unquote(u.split('/')[-1]);
  if not p.exists():
   with urllib.request.urlopen(u,timeout=120) as r,p.open('wb') as f:
    while True:
     b=r.read(1024*1024)
     if not b:break
     f.write(b)
  result.append({'source':k,'url':u,'file':p.name,'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
 print(k,'downloaded',flush=True);return result
with concurrent.futures.ThreadPoolExecutor(6) as pool:
 results=list(pool.map(get,['guns','reload','steps','clicks','ui','impacts']))
(folder/'downloads.json').write_text(json.dumps(results,indent=2))
