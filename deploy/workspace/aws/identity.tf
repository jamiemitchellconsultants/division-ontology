locals {
  api_identity = {
    Workspace__Identity__Provider = "Entra"
    Workspace__Identity__Issuer   = var.entra_issuer
    Workspace__Identity__Audience = var.entra_audience
  }
}
