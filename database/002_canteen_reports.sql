DO $$ BEGIN
IF NOT EXISTS(SELECT FROM schema_versions WHERE version=2) THEN
CREATE TABLE canteen_settings(id integer PRIMARY KEY CHECK(id=1),data jsonb NOT NULL);
CREATE TABLE canteen_inputs(id varchar(64) PRIMARY KEY,kind varchar(100) NOT NULL,date date NOT NULL,name varchar(500) NOT NULL,reference varchar(200) NOT NULL,amount numeric(19,2) NOT NULL,quantity numeric(19,4) NOT NULL CHECK(quantity>=0),"unitCost" numeric(19,4) NOT NULL CHECK("unitCost">=0),details text NOT NULL,classification varchar(30) NOT NULL,"seriesFrom" varchar(100) NOT NULL,"seriesTo" varchar(100) NOT NULL,count integer NOT NULL CHECK(count>=0));
CREATE INDEX ON canteen_inputs(kind,date);
CREATE TABLE canteen_submissions(id varchar(64) PRIMARY KEY,category varchar(10) NOT NULL CHECK(category IN('A-1','A-2')),"periodEnd" date NOT NULL,"recordsReceivedOn" date,"accountingRoutedOn" date,"auditorTransmittedOn" date,reference varchar(200) NOT NULL,remarks text NOT NULL,UNIQUE(category,"periodEnd"),CHECK("accountingRoutedOn" IS NULL OR ("recordsReceivedOn" IS NOT NULL AND "accountingRoutedOn">="recordsReceivedOn")),CHECK("auditorTransmittedOn" IS NULL OR ("accountingRoutedOn" IS NOT NULL AND "auditorTransmittedOn">="accountingRoutedOn")));
INSERT INTO schema_versions(version) VALUES(2);
END IF; END $$;
