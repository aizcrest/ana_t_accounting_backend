-- PostgreSQL schema version 1. Migration and ledger writes run transactionally.
CREATE TABLE IF NOT EXISTS schema_versions(version integer PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());
DO $$ BEGIN
IF NOT EXISTS(SELECT FROM schema_versions WHERE version=1) THEN
CREATE TABLE accounts (
 code varchar(80) PRIMARY KEY, name varchar(200) NOT NULL,
 category varchar(20) NOT NULL CHECK(category IN('Asset','Liability','Equity','Revenue','Expense')),
 level integer NOT NULL CHECK(level BETWEEN 1 AND 8), parent varchar(80) REFERENCES accounts(code) DEFERRABLE INITIALLY DEFERRED,
 CHECK((level=1 AND parent IS NULL) OR (level>1 AND parent IS NOT NULL))
);
CREATE TABLE cards(id varchar(64) PRIMARY KEY,name varchar(200) NOT NULL,type varchar(20) NOT NULL CHECK(type IN('Customer','Supplier','Employee','Other')),tin varchar(40) NOT NULL,address varchar(500) NOT NULL);
CREATE TABLE items(id varchar(64) PRIMARY KEY,name varchar(200) NOT NULL,kind varchar(30) NOT NULL,price numeric(19,2) NOT NULL CHECK(price>=0),"revenueAccount" varchar(80) NOT NULL REFERENCES accounts(code) DEFERRABLE INITIALLY DEFERRED,"expenseAccount" varchar(80) NOT NULL REFERENCES accounts(code) DEFERRABLE INITIALLY DEFERRED);
CREATE TABLE signatory_assignments(role varchar(40) PRIMARY KEY CHECK(role IN('Prepared by','Checked by','Verified by','Noted by','Approver')),name varchar(200) NOT NULL);
CREATE TABLE module_access(module varchar(100) PRIMARY KEY,view boolean NOT NULL,"create" boolean NOT NULL,post boolean NOT NULL);
CREATE TABLE atc_codes(code varchar(20) PRIMARY KEY,description varchar(500) NOT NULL,source varchar(1000) NOT NULL,verification varchar(500) NOT NULL);
CREATE TABLE documents (
 id varchar(64) PRIMARY KEY,number varchar(40) NOT NULL UNIQUE,
 type varchar(40) NOT NULL CHECK(type IN('Purchase Request','Purchase Order','Sales Invoice','Collection','Accounts Payable','Disbursement','Adjustment/Journal')),
 date date NOT NULL,"cardId" varchar(64) REFERENCES cards(id) DEFERRABLE INITIALLY DEFERRED,"sourceId" varchar(64) REFERENCES documents(id) DEFERRABLE INITIALLY DEFERRED,
 memo varchar(1000) NOT NULL,amount numeric(19,2) NOT NULL CHECK(amount>0),status varchar(20) NOT NULL CHECK(status IN('Draft','Posted')),CHECK("sourceId" IS NULL OR "sourceId"<>id)
);
CREATE TABLE journal_lines(document_id varchar(64) NOT NULL REFERENCES documents(id) DEFERRABLE INITIALLY DEFERRED,line_number integer NOT NULL,account varchar(80) NOT NULL REFERENCES accounts(code) DEFERRABLE INITIALLY DEFERRED,debit numeric(19,2) NOT NULL,credit numeric(19,2) NOT NULL,"cardId" varchar(64) REFERENCES cards(id) DEFERRABLE INITIALLY DEFERRED,PRIMARY KEY(document_id,line_number),CHECK(debit>=0 AND credit>=0 AND NOT(debit>0 AND credit>0)));
CREATE TABLE document_signatories(document_id varchar(64) NOT NULL REFERENCES documents(id) DEFERRABLE INITIALLY DEFERRED,role varchar(40) NOT NULL,name varchar(200) NOT NULL,PRIMARY KEY(document_id,role));
CREATE TABLE store_state(id integer PRIMARY KEY CHECK(id=1),revision bigint NOT NULL);
INSERT INTO store_state VALUES(1,0);
CREATE INDEX ON documents(status,date); CREATE INDEX ON documents("sourceId"); CREATE INDEX ON documents("cardId"); CREATE INDEX ON journal_lines(account,"cardId");
INSERT INTO schema_versions(version) VALUES(1);
END IF; END $$;
