locals {
  api_identity = {
    Workspace__Identity__Provider = "Entra"
    Workspace__Identity__Issuer   = var.entra_issuer
    Workspace__Identity__Audience = var.entra_audience
    # The docs UI at /api/docs signs in with the web UI's SPA registration and API scope.
    Workspace__Docs__ClientId = var.entra_client_id
    Workspace__Docs__Scopes   = var.entra_api_scope
  }
}
