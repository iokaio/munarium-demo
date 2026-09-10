# Operator-owned Azure hosting

You can host the same images in an Azure environment you create and administer. The repository contains no deployment subscription, registry name, resource group, app name, or private Server origin. Supply every resource identity from your own configuration.

Create a resource group, registry, Container Apps environment, durable PostgreSQL/source storage, and persistent visitor storage. Configure private service connectivity, a single web replica, HTTPS ingress, secret references, Production environment variables from the [hosting guide](deployment.md), and `/livez` and `/readyz` probes. Keep state files and generated outputs private. Confirm that your storage choice supports SQLite's locking requirements; prefer durable storage appropriate to a single writer and rehearse recovery.

Once those resources exist, the parameterized image update command is:

```powershell
./deploy.ps1 -Tag $releaseTag -AppName $myDemoApp -ResourceGroup $myResourceGroup -Registry $myRegistry
```

It builds the web image, authenticates to your registry through your Azure CLI session, pushes it, resolves its digest, updates the specified app, and verifies the revision and backend readiness. It does not provision infrastructure or discover another operator's resources. For a local image-only check, use `./deploy.ps1 -Tag local -BuildOnly`.

Record the previous image digest and configuration before updating. Configure identity/RBAC and registry pull access in your own environment. Deployment is an operator action; contribution CI has no Azure identity or deployment credential.
