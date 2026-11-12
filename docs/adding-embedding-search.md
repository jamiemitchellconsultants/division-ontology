# Configure embedding search

The catalog build and C4 pilot work without an embedding model. If you deploy the runtime knowledge workspace, its semantic search uses an embedding provider to find published concepts, graph nodes and annotations by meaning. Similarity helps users discover records while their identities and provenance stay distinct.

## Configure the workspace

Use the [workspace demo](../README.md#see-it-with-sample-data) to try the supplied local model and index with disposable data. For your own deployment, choose the configuration closest to your environment.

| Deployment | Supplied embedding configuration |
| --- | --- |
| [Local Docker](../deploy/workspace/local/compose.yml) | Ollama serves Qwen3 Embedding 0.6B at the internal `/v1/embeddings` endpoint. The profile uses 1,024 dimensions. |
| [Private LocalAI](../deploy/workspace/home-lab/deployment-contract.md) | Set `WORKSPACE_EMBEDDING_ENDPOINT`, `WORKSPACE_EMBEDDING_MODEL` and `WORKSPACE_EMBEDDING_REVISION` in your deployment environment. The supplied Compose profile expects Qwen3 Embedding 8B Q4 and 4,096 dimensions. |
| [AWS](../deploy/workspace/aws/application.tf) | Terraform supplies the Bedrock region and configures Amazon Titan Text Embeddings V2 with 1,024 dimensions. The workloads invoke it using their AWS roles. |

1. For a Qwen deployment, set `Workspace__Embeddings__Provider=Qwen` and `Workspace__Embeddings__Endpoint` to the full embedding URL reachable from both the API and the indexer. The adapter sends JSON with `model` and `input` and reads `data[0].embedding`. It sends no authentication header. Keep the endpoint on a private network, or add authentication support in the adapter if the service requires it.
2. Set `Workspace__Embeddings__Model` to the served model identifier, `Workspace__Embeddings__Revision` to an immutable model revision, and `Workspace__Embeddings__Dimensions` to the vector length. Apply the deployment settings to the API and indexer together. The indexer records the profile and the API reads the active profile for queries. For Bedrock, use the supplied Terraform configuration. The adapter supports the Titan V2 model and 1,024 dimensions.
3. Publish a snapshot and let the indexer finish building its baseline search index. If activation returns `snapshotIndexPending`, check the indexer logs and retry after indexing. After a model or revision change, allow the replacement index to build and verify search using the resulting active profile. Vectors from different profiles are kept separate. See the [runtime workspace runbook](features/runtime-knowledge-workspace/05-runbook.md) for publishing and the [embedding architecture decision](adr/0006-configure-identity-and-embeddings-by-environment.md) for profile behaviour.
4. Try representative business queries and check the expected entities, annotations and source references. Include synonyms, similarly named concepts and a query with no expected match. Check that service errors and pending indexing are visible.

## Extend the integration for another provider

Start with the catalog and existing search. Collect representative searches where users describe a concept in different words from its label or definition, and record the expected results. Embeddings can help with those searches, especially as the inventory grows. They add a model service, an index and operational cost, and should be evaluated against the existing search on your own examples.

An embedding model converts text into vectors that can be compared for similarity. It does not need to be a conversational model. Similarity is a discovery aid: never automatically create an equivalence assertion, reviewed link or other ontology relationship from a match. Keep source-owned IRIs distinct even when labels match.

## Choose and connect a provider

1. Choose a model and hosting arrangement appropriate to your data: a locally hosted service or a hosted provider. Check the provider's data handling, retention, access controls, availability, cost and permitted use before sending definitions or annotations to it.
2. Check the model's language coverage, input length, vector dimensions and whether it requires different instructions or prefixes for indexed text and queries. Use the same model and compatible settings for both. Test its API, authentication, rate limits and batch limits with representative text.
3. To extend the workspace, implement its [embedding provider interface](../runtime/RuntimeKnowledge.Contracts/Search.cs) and register the adapter in the [provider factory](../runtime/RuntimeKnowledge.Providers/TitanEmbeddings.cs). Configure its endpoint, model identity and other provider settings for each deployment. Obtain credentials from a secret manager or the deployment environment. Do not commit credentials or put them in command arguments.
4. Decide how the application reports provider failures, timeouts and rate limits. A search must not silently claim complete semantic coverage when the provider or index is unavailable. Keep the existing catalog usable independently of the embedding service where the application permits it.

## Build and maintain an index in a separate application

1. Choose a vector store and define an index record for each searchable entity or annotation. Compose the text to embed deliberately, for example from its label and definition; keep the original text available for display. Include annotations only when their access rules permit them to be indexed and returned.
2. Store the source-owned IRI, source repository and commit, published snapshot, record type and relevant annotation identity alongside each vector. Do not use a label as an identity or merge records merely because they have similar vectors.
3. Generate vectors from a validated catalog snapshot and, if applicable, authorised active annotations. Index queries with the same model and text preparation used for the corresponding records. Return the source and snapshot with each result so users can inspect its evidence.
4. Define how catalog refreshes, annotation changes, removals and access changes update or remove index records. Build and validate a replacement index before making it active so a failed update does not expose a partial snapshot. Preserve the last usable index and make its snapshot and staleness visible.
5. Record the model version, dimensions and text preparation with the index. Rebuild the full index when a model or other change makes existing vectors incompatible; do not mix incompatible vectors. Plan capacity, backups and recovery for the index and treat it as potentially containing sensitive information derived from source text.

## Verify a custom search integration

Implement query embedding and similarity search in the application, respecting the same authorisation rules as the underlying entities and annotations. Consider combining semantic results with exact IRI, label and keyword matches so precise searches remain useful. Show enough context and provenance for a reader to judge each result; similarity scores are not proof of meaning or equivalence.

Before deployment, test with the representative searches collected above, including synonyms, exact identifiers, similar but distinct concepts, no-match cases and restricted annotations. Compare results with text search and decide whether the improvement justifies the operating cost. Exercise provider outages, index updates and model replacement, and check that no unauthorised content is returned. Monitor quality, availability, latency and cost as the catalog and its users change.
