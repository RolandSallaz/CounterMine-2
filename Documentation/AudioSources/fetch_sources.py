import urllib.request,re,concurrent.futures
pages={'guns':'https://opengameart.org/content/the-free-firearm-sound-library','reload':'https://opengameart.org/content/gun-reload-sounds','steps':'https://opengameart.org/content/footsteps-on-different-surfaces','clicks':'https://opengameart.org/content/equipment-clicks-iii','ui':'https://kenney.nl/assets/interface-sounds','impacts':'https://kenney.nl/assets/impact-sounds'}
def fetch(kv):
 k,u=kv
 try:
  s=urllib.request.urlopen(u,timeout=30).read().decode();open('Documentation/AudioSources/'+k+'.html','w',encoding='utf8').write(s)
  return k,[x for x in re.findall(r'href=[\"\']([^\"\']+)',s) if any(t in x.lower() for t in ['.wav','.zip','.7z','download','assets.kenney'])]
 except Exception as e:return k,str(e)
for r in concurrent.futures.ThreadPoolExecutor(6).map(fetch,pages.items()):print(r)
