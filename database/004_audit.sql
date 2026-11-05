CREATE TABLE IF NOT EXISTS tenant_identity(singleton boolean PRIMARY KEY DEFAULT true CHECK(singleton),school_id uuid NOT NULL UNIQUE);
CREATE TABLE IF NOT EXISTS audit_events(id uuid PRIMARY KEY,school_id uuid NOT NULL,actor text NOT NULL,action text NOT NULL,device_id text,correlation_id text NOT NULL,at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS audit_outbox(event_id uuid PRIMARY KEY REFERENCES audit_events(id),delivered_at timestamptz);
CREATE OR REPLACE FUNCTION ledger_append_audit(event_id uuid,actor_id text,event_action text,device text,correlation text) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE school uuid;
BEGIN
 SELECT school_id INTO STRICT school FROM public.tenant_identity WHERE singleton=true;
 INSERT INTO public.audit_events(id,school_id,actor,action,device_id,correlation_id) VALUES(event_id,school,actor_id,event_action,device,correlation);
 INSERT INTO public.audit_outbox(event_id) VALUES(event_id);
END $$;
REVOKE ALL ON FUNCTION ledger_append_audit(uuid,text,text,text,text) FROM PUBLIC;

INSERT INTO schema_versions(version) VALUES(4) ON CONFLICT(version) DO NOTHING;
