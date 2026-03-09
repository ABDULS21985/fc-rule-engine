const binDir = "/Users/mac/codes/fcs/.regos-bin";
const cwd = "/Users/mac/codes/fcs/FC Engine";

function dotnetApp(name) {
  return {
    name,
    script: binDir + "/" + name,
    cwd,
    interpreter: "none",
    watch: false,
  };
}

module.exports = {
  apps: [
    {
      ...dotnetApp("regos-migrator"),
      autorestart: false,
      env: { DOTNET_ENVIRONMENT: "Development" },
    },
    {
      ...dotnetApp("regos-admin"),
      autorestart: true,
      max_restarts: 5,
      restart_delay: 3000,
      env: {
        ASPNETCORE_ENVIRONMENT: "Development",
        ASPNETCORE_URLS: "http://localhost:5001",
      },
    },
    {
      ...dotnetApp("regos-portal"),
      autorestart: true,
      max_restarts: 5,
      restart_delay: 3000,
      env: {
        ASPNETCORE_ENVIRONMENT: "Development",
        ASPNETCORE_URLS: "http://localhost:5002",
      },
    },
    {
      ...dotnetApp("regos-api"),
      autorestart: true,
      max_restarts: 5,
      restart_delay: 3000,
      env: {
        ASPNETCORE_ENVIRONMENT: "Development",
        ASPNETCORE_URLS: "http://localhost:5003",
      },
    },
    {
      ...dotnetApp("regos-regulator"),
      autorestart: true,
      max_restarts: 5,
      restart_delay: 3000,
      env: {
        ASPNETCORE_ENVIRONMENT: "Development",
        ASPNETCORE_URLS: "http://localhost:5004",
      },
    },
  ],
};
