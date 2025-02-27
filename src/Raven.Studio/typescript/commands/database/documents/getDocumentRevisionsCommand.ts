import commandBase = require("commands/commandBase");
import database = require("models/resources/database");
import document = require("models/database/documents/document");
import endpoints = require("endpoints");

class getDocumentRevisionsCommand extends commandBase {

    constructor(private id: string, private db: database | string, private skip: number, private take: number, private metadataOnly: boolean = false) {
        super();

        if (!id) {
            throw new Error("Must specify ID");
        }
    }

    execute(): JQueryPromise<pagedResult<document>> {
        const args = {
            id: this.id, 
            start: this.skip,
            pageSize: this.take,
            metadataOnly: this.metadataOnly
        };

        const url = endpoints.databases.revisions.revisions + this.urlEncodeArgs(args);

        return this.query(url, null, this.db, (results: resultsWithTotalCountDto<documentDto>) => ({
            items: results.Results.map(x => new document(x)),
            totalResultCount: results.TotalResults
        }));
    }
 }

export = getDocumentRevisionsCommand;
