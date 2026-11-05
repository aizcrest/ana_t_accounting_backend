DO $$ BEGIN
IF NOT EXISTS(SELECT FROM schema_versions WHERE version=3) THEN
CREATE TABLE banks(id varchar(64) PRIMARY KEY,name varchar(200) NOT NULL,branch varchar(200) NOT NULL,"accountName" varchar(200) NOT NULL,"accountNumber" varchar(80) NOT NULL,"glAccount" varchar(80) NOT NULL UNIQUE REFERENCES accounts(code) DEFERRABLE INITIALLY DEFERRED,active boolean NOT NULL);
CREATE TABLE check_ranges(id varchar(64) PRIMARY KEY,"bankId" varchar(64) NOT NULL REFERENCES banks(id) DEFERRABLE INITIALLY DEFERRED,prefix varchar(12) NOT NULL,"startNumber" bigint NOT NULL,"endNumber" bigint NOT NULL,"numberWidth" integer NOT NULL,description varchar(300) NOT NULL,active boolean NOT NULL,CHECK("startNumber">0 AND "endNumber">="startNumber" AND "endNumber"<=999999999999),CHECK("numberWidth" BETWEEN 1 AND 12));
ALTER TABLE canteen_inputs ADD COLUMN "bankId" varchar(64) REFERENCES banks(id) DEFERRABLE INITIALLY DEFERRED;
CREATE INDEX ON canteen_inputs("bankId",date);
INSERT INTO schema_versions(version) VALUES(3);
END IF; END $$;
