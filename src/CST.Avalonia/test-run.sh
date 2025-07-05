#!/bin/bash

# Run the CST Avalonia application and capture output
echo "Starting CST Avalonia application..."
echo "This will capture any error messages during startup."
echo "Press Ctrl+C to stop if the application hangs."
echo "-------------------------------------------"

# Run the application and capture both stdout and stderr
dotnet run 2>&1 | tee cst-run-output.log

echo "-------------------------------------------"
echo "Application exited. Check cst-run-output.log for any error messages."