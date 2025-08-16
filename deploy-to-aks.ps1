# Deploy EinAutomation API to AKS
# This script builds and deploys the .NET application with ChromeDriver support

param(
    [string]$Registry = "corpnetformpalacrprd.azurecr.io",
    [string]$ImageName = "ein-automation-app",
    [string]$Tag = "V2.0.0"
)

$ErrorActionPreference = "Stop"

Write-Host "🚀 Starting deployment to AKS..." -ForegroundColor Green

$FullImageName = "$Registry/$ImageName`:$Tag"

# Function to print colored output
function Write-Status {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[WARNING] $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

# Check if Docker is running
try {
    docker info | Out-Null
} catch {
    Write-Error "Docker is not running. Please start Docker and try again."
    exit 1
}

# Check if kubectl is available
try {
    kubectl version --client | Out-Null
} catch {
    Write-Error "kubectl is not installed or not in PATH."
    exit 1
}

# Check if connected to AKS cluster
try {
    kubectl cluster-info | Out-Null
} catch {
    Write-Error "Not connected to Kubernetes cluster. Please run 'az aks get-credentials' first."
    exit 1
}

Write-Status "Building Docker image with ChromeDriver support..."
docker build -t $FullImageName .

Write-Status "Pushing image to registry..."
docker push $FullImageName

Write-Status "Applying Kubernetes configurations..."

# Apply configurations in order
Write-Status "Applying Azure Identity..."
kubectl apply -f azure-identity.yaml

Write-Status "Applying Secret Provider Class..."
kubectl apply -f secret-provider-class.yaml

Write-Status "Applying RBAC and Deployment..."
kubectl apply -f deployment-rbac.yaml

Write-Status "Applying Service..."
kubectl apply -f service.yaml

Write-Status "Waiting for deployment to be ready..."
kubectl wait --for=condition=available --timeout=300s deployment/corpnet-formpal-aad-prod-24 -n einautomation-api

Write-Status "Checking pod status..."
kubectl get pods -l app=corpnet-formpal-aad-prod-24 -n einautomation-api

Write-Status "Getting service external IP..."
kubectl get service ein-service -n einautomation-api

Write-Status "✅ Deployment completed successfully!"

Write-Host ""
Write-Host "📋 Useful commands:" -ForegroundColor Cyan
Write-Host "  View logs: kubectl logs -f deployment/corpnet-formpal-aad-prod-24 -n einautomation-api"
Write-Host "  Check pods: kubectl get pods -l app=corpnet-formpal-aad-prod-24 -n einautomation-api"
Write-Host "  Check services: kubectl get services -n einautomation-api"
Write-Host "  Port forward: kubectl port-forward service/ein-service 8080:80 -n einautomation-api"
Write-Host "  Delete deployment: kubectl delete -f deployment-rbac.yaml"
Write-Host ""
Write-Host "🔍 ChromeDriver verification commands:" -ForegroundColor Yellow
Write-Host "  Check ChromeDriver: kubectl exec -it <pod-name> -n einautomation-api -- ls -la /usr/bin/chromedriver"
Write-Host "  Test ChromeDriver: kubectl exec -it <pod-name> -n einautomation-api -- /usr/bin/chromedriver --version"
Write-Host ""
