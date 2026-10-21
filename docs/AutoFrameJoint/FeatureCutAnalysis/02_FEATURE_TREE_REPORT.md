# Feature Tree Report

## SOLIDWORKS API result

**0 Parts were successfully opened and read.** Seven handoff SLDPRTs were passed to `OpenDoc6` with silent/read-only flags; each call returned `null` with error/warning values `0/0`. No `ModelDoc2` or Feature object was obtained. Unprivileged COM activation returned `E_ACCESSDENIED`; elevated and version-specific activation encountered COM `TYPE_E_ELEMENTNOTFOUND` behavior.

The seven attempted Parts are the six 1618a Parts and `4114-04-HN-C07-12_New.SLDPRT`. The eight local 3615a Parts were inventoried but not attempted via API.

For every Part, feature order, names/types, configuration, suppression, parent/child links, sketch, plane, references, body scope, and End Condition are `NOT_AVAILABLE`. This does not mean there is no feature history or that a model is imported-only.
