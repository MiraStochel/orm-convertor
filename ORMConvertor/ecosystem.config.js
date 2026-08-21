module.exports = {
    apps: [
      {
        name: "orm",
        script: "dotnet",
        // The --project path below is relative, so the process must run from this
        // directory no matter where `pm2 start` was invoked.
        cwd: __dirname,
        args: [
          "run",
          "--configuration", "Release",
          "--launch-profile", "http",
          "--project",
          "ORMConvertorAPI/ORMConvertorAPI.csproj"
        ],
        time: true,
        watch: false,
        env: {
          // An inherited `Version` variable would surface as an MSBuild property
          // inside `dotnet run` and override the assembly version; blank both
          // spellings, because env vars are case-sensitive outside Windows.
          version: "",
          Version: ""
        }
      }
    ]
  };
  