# Remaining Samurai grounding review

Execution02,03,04,05,07,08,09 baked and passed independent 361 Hz
validation on both avatar assignments and both lane directions: 76,616 body
clearance measurements, zero violations. The worst observed clearance was
0.009377377 m, and the largest evaluated correction was 0.2949984 m.
This establishes sampled floor clearance only, not contact, foot support,
recovery, choreography or gameplay acceptance.

The initial Execution06 and Execution10 bake did not save grounding assets. Their Mankey receiver
tracks required maximum lifts of 0.302636057 m and 0.304644942 m respectively,
including the 0.01 m cushion and adjacent-sample envelope, exceeding the unchanged
0.3 m correction bound. All nine executions were attempted; the collection
reports preserve the initial failure for these two omissions.

The focused adaptation uses a 0.005 m positive floor cushion for only
Mankey-as-receiver in these two executions. The rebaked tracks require
0.297636062 m and 0.299644947 m at independent validation samples. The correction
bound and independent clearance acceptance threshold remain unchanged. Both
executions passed four actual pair cases / eight actor-role cases, with worst
clearances 0.004984870 m and 0.004929093 m respectively and zero violations.
See Execution06And10GroundingBake.txt and Execution06And10GroundingValidation.txt.
The original collection failure reports preserve the initial result; all nine
individual validation reports now pass. Rendered support and blade-contact
review remain necessary before gameplay acceptance.
