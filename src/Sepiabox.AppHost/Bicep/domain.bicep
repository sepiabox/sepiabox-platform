// The managed certificate of the web's custom domain (S-035, D-065). Azure issues it only for a host name
// that is already on the app and whose CNAME points at it, so it is created on the run that sets
// customDomainBinding to SniEnabled, after an earlier run added the host name with its binding disabled.
// The web's own module then binds it (AzureInfrastructure.ConfigureWebApp). Empty customDomain: nothing.

@description('Not used: the certificate must be in the environment\'s region, pinned below (D-002). azd passes it.')
param location string = resourceGroup().location

param environmentName string

@description('The web\'s custom domain, such as app.sepiabox.com; empty for none.')
param customDomain string = ''

@description('Disabled until the DNS records exist and the host name was added; then SniEnabled.')
@allowed([
  'Disabled'
  'SniEnabled'
])
param customDomainBinding string = 'Disabled'

param certificateName string

resource environment 'Microsoft.App/managedEnvironments@2025-07-01' existing = {
  name: environmentName
}

resource certificate 'Microsoft.App/managedEnvironments/managedCertificates@2025-07-01' = if (!empty(customDomain) && customDomainBinding == 'SniEnabled') {
  parent: environment
  name: certificateName
  location: 'francecentral'
  properties: {
    subjectName: customDomain
    domainControlValidation: 'CNAME'
  }
}
