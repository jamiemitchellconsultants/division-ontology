// An infrastructure failure says nothing about a source's content: it fails the whole run
// and leaves dist/ untouched, instead of becoming an import status (ADR 0011).
export class InfrastructureError extends Error {
  constructor(message, options) {
    super(message, options);
    this.name = 'InfrastructureError';
  }
}
