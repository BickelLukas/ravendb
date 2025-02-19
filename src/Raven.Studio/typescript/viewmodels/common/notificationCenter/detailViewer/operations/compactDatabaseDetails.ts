import app = require("durandal/app");

import operation = require("common/notifications/models/operation");
import abstractNotification = require("common/notifications/models/abstractNotification");
import notificationCenter = require("common/notifications/notificationCenter");
import abstractOperationDetails = require("viewmodels/common/notificationCenter/detailViewer/operations/abstractOperationDetails");

type compactListItemStatus = "processed" | "skipped" | "processing" | "pending";

type compactStageDetailedProgress = {
    treeName: string;
    processed: number;
    total: number;
}

type compactListItem = {
    name: string;
    stage: compactListItemStatus;
    processed: number;
    total: number;
    detailedProgress: compactStageDetailedProgress;
}

class compactDatabaseDetails extends abstractOperationDetails {

    view = require("views/common/notificationCenter/detailViewer/operations/compactDatabaseDetails.html");

    detailsVisible = ko.observable<boolean>(false);
    tail = ko.observable<boolean>(false);

    items: KnockoutComputed<Array<compactListItem>>;
    messages: KnockoutComputed<Array<string>>;
    previousProgressMessages: string[];
    
    constructor(op: operation, notificationCenter: notificationCenter) {
        super(op, notificationCenter);
        this.initObservables();
    }

    protected initObservables() {
        super.initObservables();
        
        this.items = ko.pureComputed(() => {
            if (this.op.status() === "Faulted") {
                return [];
            }

            const status = (this.op.isCompleted() ? this.op.result() : this.op.progress()) as Raven.Client.ServerWide.Operations.CompactionProgressBase<Raven.Client.ServerWide.Operations.CompactionProgress | Raven.Client.ServerWide.Operations.CompactionResult>;

            if (!status) {
                return [];
            }

            const result: compactListItem[] = [];

            if (status.IndexesResults) {
                for (const indexName of Object.keys(status.IndexesResults))
                {
                    result.push(this.mapToCompactItem(indexName, status.IndexesResults[indexName]));
                }
            }
            
            result.push(this.mapToCompactItem("Documents", status));

            return result;
        });

        this.messages = ko.pureComputed(() => {
            if (this.operationFailed()) {
                const errors = this.errorMessages();
                const previousMessages = this.previousProgressMessages || [];
                return previousMessages.concat(...errors);
            } else if (this.op.isCompleted()) {
                const result = this.op.result() as Raven.Client.ServerWide.Operations.CompactionResult;
                const messages: string[] = [];

                if (result) {
                    if (result.IndexesResults) {
                        for (const indexResult of Object.keys(result.IndexesResults)) {
                            const indexMessages = (result.IndexesResults[indexResult] as Raven.Client.ServerWide.Operations.CompactionResult).Messages;
                            messages.push(...indexMessages.map(x => "[" + indexResult + "] " + x));
                        }
                    }
                    
                    messages.push(...result.Messages);
                }
                
                return messages;
            } else {
                const progress = this.op.progress() as Raven.Client.ServerWide.Operations.CompactionResult;
                if (progress) {
                    this.previousProgressMessages = progress.Messages;
                }
                return progress ? progress.Messages : [];
            }
        });

        this.registerDisposable(this.messages.subscribe(() => {
            if (this.tail()) {
                this.scrollDown();
            }
        }));

        this.registerDisposable(this.tail.subscribe(enabled => {
            if (enabled) {
                this.scrollDown();
            }
        }));

        this.registerDisposable(this.operationFailed.subscribe(failed => {
            if (failed) {
                this.detailsVisible(true);
            }
        }));

        if (this.operationFailed()) {
            this.detailsVisible(true);
        }
    }
    
    private scrollDown() {
        const messages = $(".compact-messages")[0];
        if (messages) {
            messages.scrollTop = messages.scrollHeight;
        }
    }

    toggleDetails() {
        this.detailsVisible(!this.detailsVisible());
    }
    
    static supportsDetailsFor(notification: abstractNotification) {
        return (notification instanceof operation) && notification.taskType() === "DatabaseCompact";
    }

    static showDetailsFor(op: operation, center: notificationCenter) {
        return app.showBootstrapDialog(new compactDatabaseDetails(op, center));
    }

    private mapToCompactItem(name: string, item: Raven.Client.ServerWide.Operations.CompactionProgressBase<Raven.Client.ServerWide.Operations.CompactionProgress | Raven.Client.ServerWide.Operations.CompactionResult>): compactListItem {
        let stage: compactListItemStatus = "pending";
        if (item.Processed) {
            if (item.Skipped) {
                stage = "skipped";
            } else {
                stage = "processed";
            }
        } else {
            if (item.TreeName) {
                stage = "processing";
            }
        }
        
        let details: compactStageDetailedProgress = null;
        
        if (item.TreeName) {
            details = {
                treeName: item.TreeName,
                total: item.TreeTotal,
                processed: item.TreeProgress
            }
        }
        
        return {
            name: name, 
            processed: item.GlobalProgress,
            total: item.GlobalTotal,
            detailedProgress: details,
            stage: stage
        }
    }
    
    static findEffectiveMessage(result: Raven.Client.ServerWide.Operations.CompactionResult) {
        if (result.Message) {
            return result.Message;
        }

        // looks like we in the middle of index compaction, try to find index being compacted

        for (const indexResult of Object.keys(result.IndexesResults)) {
            if (result.IndexesResults[indexResult].Processed || result.IndexesResults[indexResult].Skipped) {
                continue;
            }

            const message = result.IndexesResults[indexResult].Message;
            if (!message) {
                continue;
            }

            return "[" + indexResult + "] " + message;
        }
        
        return "";
    }

    static merge(existing: operation, incoming: Raven.Server.NotificationCenter.Notifications.OperationChanged): boolean {
        if (!compactDatabaseDetails.supportsDetailsFor(existing)) {
            return false;
        }

        const isUpdate = incoming !== undefined;

        if (!isUpdate) {
            // object was just created - only copy message -> message field

            if (!existing.isCompleted()) {
                const result = existing.progress() as Raven.Client.ServerWide.Operations.CompactionResult;
                result.Messages = [compactDatabaseDetails.findEffectiveMessage(result)];
            }
        } else if (incoming.State.Status === "InProgress") { // if incoming operation is in progress, then merge messages into existing item
            const incomingResult = incoming.State.Progress as Raven.Client.ServerWide.Operations.CompactionResult;
            const existingResult = existing.progress() as Raven.Client.ServerWide.Operations.CompactionResult;
            incomingResult.Messages = existingResult.Messages.concat(compactDatabaseDetails.findEffectiveMessage(incomingResult));
        }

        if (isUpdate) {
            existing.updateWith(incoming);
        }
        
        return true;
    }
}

export = compactDatabaseDetails;
