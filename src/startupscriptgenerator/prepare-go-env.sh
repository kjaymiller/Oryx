#!/bin/sh

# If we're running on Alpine (i.e. apk is available), then most likely these two packages are missing
if type "apk" > /dev/null; then
	apk add --no-cache git
	apk add --no-cache build-base
fi

go get github.com/Microsoft/ApplicationInsights-Go/appinsights
go get github.com/Microsoft/ApplicationInsights-Go/appinsights/contracts
