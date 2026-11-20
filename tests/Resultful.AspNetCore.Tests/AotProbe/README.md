# Native AOT probe

Not in the solution and not run by CI. Publish with native AOT on `net11.0` (warnings are errors, so any IL2xxx or
IL3xxx warning fails the publish), then start the binary and request each route:

```
dotnet publish tests/Resultful.AspNetCore.Tests/AotProbe -c Release -r linux-x64 -o temp/aotprobe
ASPNETCORE_URLS=http://127.0.0.1:5899 temp/aotprobe/Resultful.AspNetCore.AotProbe
curl -i http://127.0.0.1:5899/orders/1   # 200 {"id":1,"name":"teapot"}
curl -i http://127.0.0.1:5899/orders/9   # 404 problem, detail "order 9 was not found"
curl -i http://127.0.0.1:5899/boom       # 500 problem, fixed title, no detail, no "hunter2"
curl -i http://127.0.0.1:5899/check      # 400 validation problem with "errors"
```
