import json,sys,urllib.request,tarfile,io,os
repo,tag,dest=sys.argv[1],sys.argv[2],sys.argv[3]
def get(url,accept=None):
    r=urllib.request.Request(url,headers={"Accept":accept} if accept else {})
    return urllib.request.urlopen(r).read()
acc="application/vnd.oci.image.index.v1+json,application/vnd.docker.distribution.manifest.list.v2+json,application/vnd.oci.image.manifest.v1+json,application/vnd.docker.distribution.manifest.v2+json"
m=json.loads(get(f"https://mcr.microsoft.com/v2/{repo}/manifests/{tag}",acc))
if "manifests" in m:
    d=[x for x in m["manifests"] if x["platform"]["architecture"]=="amd64" and x["platform"]["os"]=="linux"][0]["digest"]
    m=json.loads(get(f"https://mcr.microsoft.com/v2/{repo}/manifests/{d}",acc))
os.makedirs(dest,exist_ok=True)
for l in m["layers"]:
    b=get(f"https://mcr.microsoft.com/v2/{repo}/blobs/{l['digest']}")
    with tarfile.open(fileobj=io.BytesIO(b)) as t:
        mem=[x for x in t.getmembers() if x.name.startswith("usr/share/dotnet/") and not x.name.split("/")[-1].startswith(".wh.")]
        for x in mem: x.name=x.name[len("usr/share/dotnet/"):]
        mem=[x for x in mem if x.name]
        t.extractall(dest,members=mem,filter="tar") if mem else None
    print("layer",l["digest"][:19],len(b))
