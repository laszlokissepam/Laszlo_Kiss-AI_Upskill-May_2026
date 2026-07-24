# Garden Buddy integration app

This minimal Angular application hosts and verifies the standalone `ChatComponent` from
`../chat-component/src/chat`.

The host configures the widget through its required `chatApiUrl` and `deploymentName`
inputs. Every host must provide both values explicitly.

## Run locally

Start the ASP.NET Core API on `http://localhost:5078`, then run:

```powershell
cd ..
npm install
npm start
```

Open `http://localhost:4200`. The integration host explicitly passes
`http://localhost:5078/api/chat` to the widget. The backend CORS policy permits local
Angular development from both `localhost:4200` and `127.0.0.1:4200`.

The development server uses Angular's standard live reload and HMR behavior.

## Verify

```powershell
npm run build
npm test -- --watch=false
```

The host and widget use SCSS. Component templates remain Angular HTML files because Angular does not compile ASP.NET Razor `.cshtml` templates. The chat template uses Angular's built-in `@if` and `@for` control-flow blocks.
