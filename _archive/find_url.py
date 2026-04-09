import urllib.request
versions=['1.7.0','1.6.0','1.5.8','1.5.5','1.4.0','1.3.0','1.2.0','1.1.0','1.0.0']
found=False
for v in versions:
    for cdn in ['https://cdn.jsdelivr.net/npm/@imgly/background-removal@{}/dist/browser/index.umd.js', 'https://unpkg.com/@imgly/background-removal@{}/dist/browser/index.umd.js']:
        url=cdn.format(v)
        try:
            r=urllib.request.urlopen(url, timeout=5)
            print('OK', url, r.getcode())
            found=True
            break
        except Exception as e:
            pass
    if found:
        break
print('found', found)
