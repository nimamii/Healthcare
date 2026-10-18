// ISecurityEventRecorder and SecurityEvent moved to the shared kernel.
//
// They were declared here first, which put a compile-time dependency from
// Audit to Identity: Audit had to reference Identity.Application to implement
// an interface Identity owned, so the "audit is a leaf service" property broke
// and any second producer of security events would have needed a reference
// into this module.
//
// Delete this file once it is no longer referenced.
