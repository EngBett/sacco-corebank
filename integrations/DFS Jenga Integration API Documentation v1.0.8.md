# **API Documentation** 

- 🎉 Welcome to our developer documentation! 🎉 

We're here to help you build the future of payment solutions with our cutting-edge technology, designed to transform the way merchants send and receive payments securely. Our powerful and lightning-fast APIs make it easy to optimize your payment processes, guaranteeing smooth and efficient transactions. 

The articles will help you seamlessly integrate our solution into your applications, so you can concentrate on scaling your business. 

##### ###Jenga API 

Jenga API provides merchants with a secure, simple and convenient way to send money via mobile and bank transfers. It also provides other value added services such as forex, account statements, IPRS checks and more 

### **Getting Started** 

For both product offerings, you will need to create an account on the Jenga Merchant Portal. The portal allows you to: 

1. Register for a sandbox account and environment 

2. Create and view authentication credentials 

3. Configure subscriptions 

and so much more. 

## **Security** 

Our products use strong military-grade security standards to protect you and your customers' data and ensure users' privacy. Security measures are implemented for both data at rest and data in transport. 

## **Data Encryption** 

Our database servers encrypt data using encryption algorithms. The encryption keys are rotated and managed in a network separated from the database and application servers. They are stored in a fault-tolerant key management cluster with limited access. The master key is kept in a secure vault to ensure maximum security. 

## **Transport Security** 

All data served over our REST API uses HTTPS. We regularly audit our security setup to ensure that the certificates we serve are current. We force HTTPS for all connections to our API server to ensure that data is always encrypted during the transport from our server to your application. You must use the same methods to ensure that data is encrypted to the end user. 

## **Logging** 

We log all the API calls and track the interactions with web services for later review. 

## **PCI-DSS & Financial Regulations** 

Depending on the type of data integrations that are necessary, JengaAPI will ensure the redaction of all sensitive financial information in accordance with the applicable laws in the Republic of Kenya. 

## **Authentication and Authorization** 

Jenga supports Bearer authentication (also called token authentication) that utilizes JSON web tokens. To get your JWT token, you must pass the API keys available from the Jenga Dashboard. 

Once you have a token, you can make subsequent requests to initiate payments, check completed transactions and more. Just pass this token in the Authorization header like so: 

HTTP GET {{your_api_path}} Accept: application/json Authorization: Bearer <your_token> 

## **API Keys** 

After you sign up on Jenga, you will be presented with three API Keys: 

- **Merchant Code** : This is the primary identifier of your Jenga account. If you require support from the Jenga Support Team, you must provide your merchant code. 

- **Consumer Secret** : This serves as the secondary identifier of your account and might be used in different cases depending on the product you are consuming; for instance, when encrypting and decrypting data passed to and from Jenga or decrypting response data on successful payment on 

##### Checkout. 

**API Key** : This is required during authentication ONLY 

Jenga offers two account modes: 

**Test Mode** : In this mode, you will use simulated processes meaning _no real money is involved_ . This mode allows you to ensure that your integration works and serves as expected. You access our Test Cards, Accounts and Mobile Numbers here 

**Live Mode** : You will transact using real money in this mode. 

In cases where you have registered on both Live and Test Mode, we recommend using different passwords for the two modes. 

## **Retrieving your API Keys** 

To retrieve your set of credentials: 

Log into your Jenga HQ portal 

Navigate to Settings 

Select the API Keys and click View Keys. A set of credentials will be revealed, which you can copy and store securely elsewhere. 

🚧 Lost your API Key? 

Not to worry - you can generate the same set of credentials again. 

Alternatively, if you want to generate a new API key and credentials, you can do so by tapping the "Generate New Key" button. 

## **Generate your Bearer token** 

Now that you have your API Keys ready, let's dive into how to generate your Bearer token on Jenga. 

All Jenga APIs are RESTFUL APIS and utilizes JSON as an exchange format, so you will need to pass the Content-Type as application/json . To authenticate on Jenga, you will need to pass two headers: 

The Content-Type and Api-Key : 

Content-Type: application/json Api-Key: API_KEY_FROM_JENGA_PORTAL 

On your request body, you need to pass two mandatory string fields: 

1. merchantCode 

2. consumerSecret 

https://uat.finserve.africa/authentication/api/v3/authenticate/merchant 

https://api.finserve.africa/authentication/api/v3/authenticate/merchant 

/* POST /authentication/api/v3/authenticate/merchant HTTP/1.1 /* Api-Key: hbWCSNsk61C24HTwI0q9olFgNGsxNP8jfVhLRo0scR... /* Content-Type: application/json*/ { "merchantCode": "0582910862", "consumerSecret": "ce0NHpa7ZaxmOFkbEbULABpu412fS4NQa" } 

##### And the response will look something like this: 

// 200 OK { "accessToken": "eyJhbGciOiJSUzUxMiJ9.eyJ0b2tlblR5cGUiOiJNRVJDSEFOVCIsImVudiI...", "refreshToken": "ctmB6GJq9Tqbf+Z5neWs/7WGA3S6nHs+VToc0J9eXdLTSVD63BrhDRSCIXu...", "expiresIn": "2023-07-13T07:03:02Z", "issuedAt": "2023-07-13T06:48:02Z", "tokenType": "Bearer" } 

## **Why signatures?** 

To ensure the security of your transactions or requests, we have implemented security controls that ensure transactions can only be initiated by you and no one else. To achieve this, we use message signatures. 

### **How it works** 

All requests, other than the Identity API, will be required to have a signature in the header. The structure of the header will be as follows: 

Signature: {{signature_in_base64}} 

To generate the signature, you will need create a key pair of private key and public key. You will share the public key with us and use the private key to generate the signature. 

### **Creating private & public keys** 

Use following command in your preferred terminal to generate a keypair with a self-signed certificate. 

In this command, we are using the openssl. You can use other tools e.g. keytool (ships with JDK - Java Developement Kit), keystore explorer e.t.c 

openssl genrsa -out privatekey.pem 2048 

Once you are successful with the above command a file (privatekey.pem) will be created on your present directory, proceed to export the public key from the keypair generated. The command below shows how to do it. 

openssl rsa -in privatekey.pem -outform PEM -pubout -out publickey.pem 

If the above command is successful, a new file (publickey.pem) will be created on your present directory. Copy the contents of this file and add it on our jengaHQ portal. **Make sure to copy only the contents of the keyblock and paste as is.** 

##### **The Generated Key Files** 

The privatekey.pem file looks something like this: 

-----BEGIN RSA PRIVATE KEY----- 

MIIEpAIBAAKCAQEA1BvVbYnuhGGmmIwUdUkFP+WG+tkXyf+o7DopD2MgDh+jwyvA jwDbSENHOwRuIzYEPBePk1lcchTDraz6VbWbwnDWJNn6cQkDCozRvuN1JnYa88Yy u7XFQyvskwpk2zgzJ3azuDYAZ0I4yBXAeamLXibOOjm9KFGrBhDMGUtQLVvayZTT iyyJnDXh5bNISjZeWU1VEiksaMUYujrXmLKDIlFM8xlJJvmvijlwS23J9oP3co3u Hhd14pGXHKOYXvyVt3Q1taFIps7zS2x2vsGCaK9cdHrExWQdF9fzN95QfagMp7f2 DSMQVhOsXTdZFXMOrkVtWOTlwUJucBGstKOjNwIDAQABAoIBADYvXhh7kgkTgSGb N2a23rZyBkdyyhb6Tsb6HJ8nrXquLoGfXbOqflo5harX+OLZ278WLcFwpKMoFsz5 

UYIvwLitZqdHYCkcKkC5tKNVLApFRaFc0n0NdHUydV8i2pz+AGNmeYbnlLbMPgEv PVpXK5lDxI8vTNlN86i7Bci4aqULSLYQ9E4/yWOAEAkp9+O7lb6HKcYQ8SgpZ9d9 M0RmxP4Qgc7HdYGo8KzvFJHFtTxOmDMOjWShAxdk77QmnZAznmpmz4v4uUbwR8YK P7oZV7Lvl1gfma/h+kYR5yd5kJtHSu2+Q2n8gLRfUeFGqD56d1O93VnhSJyhI/OR zqNhZjECgYEA+zr273pOXDTHRbLVOrYDjxHY6DnD/OeV+qaiCOVFIjTdWAITMuvr NwKn+Ez0nSBCmKiozdcOzzjxllRs7BhhNGwlQlJloJurfbGIbKb0lSFRQD0bXcxY 32lVscKotMP4lQmmTlALUxbPaCt/MmBsgXg7IA+lGe+I5evAOImE1tUCgYEA2CK6 uUtO6EUBYRC88oyXdlh5lsEM6GpsMHFIlHLeW2EmxTbaaWVf91LRg7lyL+UXEWQ6 zy/oZDuXZI3EVQN2Po2Pcs9V4GFa9FO8YODzdoaHos+f9pe841enZNHkSkXvWXKv j9HjvVsoKGq3Lg9acLx9dMVmx8ilT1FvDcMz79sCgYBD4soJKgZ0mfpi1hESPU62 4T64eauA8l8vjMlqF/HXbWuGNYFUmDVF9xzGVp0evDHiqGh8vqkMy7lUQtnv7iKO FM74neVCQe5UF53ipjae+ZLIBfsYHHjDXeY/E3ec6PuJ4kKjFLQKrrY60s4bIb0Q OxnW7wNQ/84BOvQFEvvnRQKBgQDCwwjf0CzawNPtU9fv+SDDVBa88llfVgcH4A03 OAuG7JSzQiqurts7UzXZLVLoNdgDo/4alWEkcU6LHfS9ZtE2rPmGy67m8tOzN4GZ CxxYwgGXhODwpOthMat1/m1pQHvebqolP02pZGtbgE5xAwTMcg3bG8byYKwWPZuF G1HB4QKBgQC5xyHCNq2jQ9YAvja6oqohx59a9Y57R+Xb1Z42w64fouZcbysVWM1f bfSXnsW49RLDH0Ynns8i5jb+LMdL6W7UujdqrgNMmcNF2GXxHqnYxD10SKRctio5 7gfs5SeS0jvcs7NLCRMhw/yol4pRg12HWcm/YsIpn/na/hUzHesJ+A== -----END RSA PRIVATE KEY----- 

###### -----BEGIN PUBLIC KEY----- 

MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA1BvVbYnuhGGmmIwUdUkF P+WG+tkXyf+o7DopD2MgDh+jwyvAjwDbSENHOwRuIzYEPBePk1lcchTDraz6VbWb wnDWJNn6cQkDCozRvuN1JnYa88Yyu7XFQyvskwpk2zgzJ3azuDYAZ0I4yBXAeamL XibOOjm9KFGrBhDMGUtQLVvayZTTiyyJnDXh5bNISjZeWU1VEiksaMUYujrXmLKD IlFM8xlJJvmvijlwS23J9oP3co3uHhd14pGXHKOYXvyVt3Q1taFIps7zS2x2vsGC aK9cdHrExWQdF9fzN95QfagMp7f2DSMQVhOsXTdZFXMOrkVtWOTlwUJucBGstKOj NwIDAQAB 

-----END PUBLIC KEY----- 

### **Generating the signature** 

The first step to generating the signature is to prepare the data to be signed. This is done by concatenating (joining) particular values within the API request payload. Different APIs will have different formulae for concatenation and this is detailed in each API's documentation. 

To illustrate this, we use the Opening and Closing Account Balance API. 

The request payload looks like this: 

{ "countryCode": "KE", "accountId": "0011547896523", "date": "2018-08-09" } 

##### **The instructions further say:** 

A SHA-256 signature to proof that this request is coming from the merchant. Build a String of concatenated values of the request fields with the following order: accountId, countryCode, date. The resulting text is then signed with Private Key and Base64 encoded. 

We can see that the data to be signed is a concatenation of the values in the fields: accountId, countryCode and date and in that order and from our payload above, that would be: 0011547896523KE2018-08-09 . This is the data we will sign. 

Now onto signing. The mechanism of signing this data will depend on the language you are using. For instance, if you are using PHP or Python , you would go about it as follows; 

<?php $plainText = "0011547896523KE2018-08-09"; // See separate instruction on how to create this concatenation $privateKey = openssl_pkey_get_private(("file://path/to/privatekey.pem")); $token = "QNg9X7cLJSpZVOpaJJ33wX0AbcRF"; openssl_sign($plainText, $signature, $privateKey, OPENSSL_ALGO_SHA256); 

from base64 import b64encode 

from Crypto.Hash import SHA256 from Crypto.Signature import PKCS1_v1_5 from Crypto.PublicKey import RSA 

message = "0011547896523KE2018-08-09".encode('utf-8') # See separate instruction on how to create this concatenation digest = SHA256.new() digest.update(message) 

private_key = False with open("privatekey.pem", "r") as myfile: private_key = RSA.importKey(myfile.read()) 

signer = PKCS1_v1_5.new(private_key) sigBytes = signer.sign(digest) signBase64 = b64encode(sigBytes) 

Your generated signature is now in a variable $signature or signBase64 in the Python example. 

You can now include it in your request as follows; 

<?php $plainText = "0011547896523KE2018-08-09"; $privateKey = openssl_pkey_get_private(("file://path/to/privatekey.pem")); $token = "QNg9X7cLJSpZVOpaJJ33wX0AbcRF"; openssl_sign($plainText, $signature, $privateKey, OPENSSL_ALGO_SHA256); 

$curl = curl_init(); $data_string = '{ "countryCode":"KE", "accountId":"0011547896523", "date":"2018-08-09" }'; 

curl_setopt_array($curl, array( CURLOPT_URL => "https://sandbox.jengahq.io/account-test/v3/accounts/accountbalance/query", CURLOPT_RETURNTRANSFER => true, CURLOPT_ENCODING => "", CURLOPT_MAXREDIRS => 10, CURLOPT_TIMEOUT => 30, CURLOPT_HTTP_VERSION => CURL_HTTP_VERSION_1_1, CURLOPT_CUSTOMREQUEST => "POST", CURLOPT_POSTFIELDS => $data_string, 

CURLOPT_HTTPHEADER => array( 

"Authorization: Bearer " . $token, "cache-control: no-cache", "Content-Type: application/json", "signature: " . base64_encode($signature) ) )); $result = curl_exec($curl); $err = curl_error($curl); curl_close($curl); if ($err) { echo "cURL Error #:" . $err; } else { echo $result; } ?> 

import json import requests 

from base64 import b64encode from Crypto.Hash import SHA256 from Crypto.Signature import PKCS1_v1_5 from Crypto.PublicKey import RSA 

message = "0011547896523KE2018-08-09".encode('utf-8') digest = SHA256.new() digest.update(message) 

private_key = False with open("privatekey.pem", "r") as myfile: private_key = RSA.importKey(myfile.read()) 

signer = PKCS1_v1_5.new(private_key) sigBytes = signer.sign(digest) signBase64 = b64encode(sigBytes) 

headers = { 

'Content-Type': 'application/json', 'Authorization': 'Bearer ipARkr578zcp2fYPiZpEL1cpG6qE', 'signature': signBase64 } params = {} payload = { "countryCode": "KE", "accountId": "0011547896523", "date": "2018-08-09" } url = 'https://sandbox.jengahq.io/account-test/v3/accounts/accountbalance/query' response = requests.post(url, headers=headers, params=params, data=json.dumps(payload)) print(response.text) 

And viola! You have done your first secure API request 

## **First API Call** 

Now that you have your API keys and Bearer token as well as your signature, you're ready to make your first API call! Let's take a quick example. 

You've initiated a request to get your account opening and closing balance from your application. 

You'll call the Opening and Closing Account Balance API and pass the account number ( accountId ) in the payload, as well as the Bearer token and signature in the Authorization Header of your request. 

POST /account-api/v3.0/accounts/accountbalance/query HTTP/1.1 Content-Type: application/json Authorization: Bearer {{token_here}} 

###### signature: {{signature_here}} 

{ "countryCode": "KE", "accountId": "0011547896523", "date": "2023-09-29" } 

##### And **hey presto** ! 🎉 

You're ready to use any of the other APIs. All you need to do is pass the parameters of the endpoint and authorize your request by passing your Bearer token in the Authorization Header plus signature, where required. 

# **DFS Partner REST APIs — Updated Documentation** 

**Version:** 1.2.0 

**OpenAPI Version:** 3.1.0 

**Last Updated:** June 2026 

**Base URL (UAT):** https://uat.finserve.africa/momo-apis 

**Base URL (Production):** https://api.finserve.africa/momo-apis 

## 📋 **Table of Contents** 

##### 1. Overview 

2. Authentication 

3. PIN & Password Encryption 

4. Base URL & Environments 

5. Standard Headers 

6. API Reference 

<u>Customer-Initiated Payment (C2B) C2B PayBill Payment C2B Validation</u> 

##### <u>C2B Confirmation</u> 

<u>Business-to-Customer Payment (B2C)</u> 

<u>B2B Buy Goods B2B Pay Bill</u> 

<u>Query Transaction Status Query Organization Balance Reverse Transaction</u> 

<u>Mini Statement</u> 

7. Response Models 

8. Error Handling 

9. Testing Guidelines 

## 🧭 **Overview** 

The **DFS Partner REST APIs** enable partners and merchants to integrate mobile money services into their applications. The platform supports payment processing, bill payments, transaction reversals, status queries, and balance inquiries. 

All API communication occurs over **HTTPS** using **JSON** format. The APIs follow RESTful design principles with standardized HTTP status codes and consistent response structures. 

## 🔐 **Authentication** 

All API requests must be authenticated using **Bearer JWT tokens** . Include the token in the Authorization header. 

|**Header**|**Value**|**Required**|
|---|---|---|
|Content-Type|application/json|Yes|
|Authorization|Bearer <jwt_token>|Yes|



**Note:** Contact the Finserve API gateway team to obtain development and production credentials. 

### **Generate your Bearer token** 

To authenticate on Jenga, you will need to pass two headers: Content-Type and Api-Key . 

POST /authentication/api/v3/authenticate/merchant Content-Type: application/json Api-Key: API_KEY_FROM_JENGA_PORTAL 

##### **Request Body:** 

|**Field**|**Type**|**Required**|**Description**|
|---|---|---|---|
|merchantCode|string|Yes|Primary identifier of your Jenga account|
|consumerSecret|string|Yes|Secondary identifier of your account|



##### **Example Request:** 

{ 

"merchantCode": "9787786667", 

"consumerSecret": "le7Q1S2Wd67Wafn1zHFvD8C7fKP7z5" 

} 

##### **Example Response:** 

{ 

"accessToken": "eyJhbGciOiJSUzUxMiJ9.eyJ0b2tlblR5cGUiOiJNRVJDSEFOVCIsImVudiI...", 

"refreshToken": "ctmB6GJq9Tqbf+Z5neWs/7WGA3S6nHs+VToc0J9eXdLTSVD63BrhDRSCIXu...", 

"expiresIn": "2023-07-13T07:03:02Z", 

"issuedAt": "2023-07-13T06:48:02Z", 

"tokenType": "Bearer" 

} 

**Environments:** 

|**Environment**|**URL**|
|---|---|
|UAT|https://uat.finserve.africa/authentication/api/v3/authenticate/merchant|
|Production|https://api.finserve.africa/authentication/api/v3/authenticate/merchant|



### **Security Scheme** 

securitySchemes: bearerAuth: type: http scheme: bearer bearerFormat: JWT 

## 🔒 **PIN & Password Encryption** 

Certain DFS APIs require sensitive credentials such as merchant PINs and organization passwords to be encrypted before transmission. These values **must not be sent in plain text** . 

### **Encryption Standard** 

Sensitive credential fields must be encrypted using **AES-256-GCM** prior to being included in the request payload. 

|**Property**|**Value**|
|---|---|
|Algorithm|AES|
|Mode|GCM|
|Padding|NoPadding|
|Key Derivation|PBKDF2WithHmacSHA256|
|AES Key Length|256 bits|



|**Property**|**Value**|
|---|---|
|PBKDF2 Iterations|65,536|
|IV Length|12 bytes|
|Salt Length|16 bytes|
|Authentication Tag Length|128 bits|
|Character Encoding|UTF-8|
|Payload Encoding|Base64|



### **Secret Key** 

The **API Key** obtained from the Jenga Merchant Portal is used as the secret key for the AES-256-GCM encryption of PINs and passwords. Retrieve your API Key by navigating to **Settings → API Keys** in the portal. 

**Note:** The API Key is the same credential used during Bearer token generation. Ensure this key is stored securely and never exposed in client-side code or version control. 

### **Encryption Process** 

1. Retrieve your **API Key** from the Jenga Merchant Portal. 

2. Generate a random 16-byte salt. 

3. Generate a random 12-byte IV. 

4. Derive the AES key using PBKDF2WithHmacSHA256 with your API Key as the password. 

5. Encrypt the credential value using AES/GCM/NoPadding. 

6. Concatenate the values in the following order: 

IV + SALT + ENCRYPTED_DATA 

7. Base64 encode the final payload. 

8. Populate the encrypted Base64 value in the request field. 

### **Security Requirements** 

A new IV **must** be generated for every request. 

A new salt **must** be generated for every request. 

Encrypted payloads **must** be Base64 encoded. 

Credential values must be UTF-8 encoded before encryption. 

Authentication tag validation failures must be treated as invalid requests. 

Plain text PINs and passwords must never be logged or persisted. 

The API Key must be stored securely and never transmitted in the request payload. 

HTTPS/TLS must always be used when invoking DFS APIs. 

### **Example Encrypted Value** 

VvD2s6HkQoD8iYtZy3xJdg== 

**Important:** All request fields marked as requiring encryption in this documentation must follow the encryption specification described in this section, using the API Key as the secret key. 

## 🌐 **Base URL & Environments** 

|**Environment**|**Base URL**|
|---|---|
|UAT / Sandbox|https://uat.finserve.africa/momo-apis|
|Production|https://api.finserve.africa/momo-apis|



## 📨 **Standard Headers** 

All requests must include: 

Content-Type: application/json Authorization: Bearer {your_jwt_token} 

## 📚 **API Reference** 

### 1️⃣ **Customer-Initiated Payment (C2B)** 

**Endpoint:** POST /api/v1/transaction/c2b/customer-initiated-payment 

##### **Tags:** Payments API 

**Operation ID:** customerInitiatedMerchantPayment 

**Description:** Customer Buy Goods — Initiates a payment from a customer's mobile money wallet to a registered merchant. 

#### **Request Parameters** 

|**Field**|**Type**|**Required**|**Description**|||||||
|---|---|---|---|---|---|---|---|---|---|
|requestId|string|Yes|Unique identifier for the request. Must follow the format<br>shortCode_uniqueRequestId. Example:<br>247247_REQ123456790|||||||
|amount|string|Yes|Payment amount. Example:<br>250|||||||
|currency|string|Yes|ISO currency code. Pattern: `^(KES|UGX|TZS|RWF|SDG|CDF|ETB)$ .<br>Example:<br>KES`|
|shortCode|string|Yes|Merchant short code. Example:<br>800800|||||||
|pin|string|Yes|Merchant PIN encrypted as described in the<br>PIN & Password<br>Encryption section. Example:<br>1pSF5lfWX+5lTX7RC1vNVLZZzoDg/vGZ9hm9JDp6ldFOLkM1|||||||
|callbackUrl|string|Yes|HTTPS endpoint for asynchronous status notifications.<br>Example:<br>https://yourapp.com/callback|||||||
|msisdn|string|Yes|Customer mobile number in international format. Example:<br>254765555186|||||||
|remarks|string|No||||||||



**Request Example** 

{ "requestId": "800800_80801234567890", "currency": "KES", "amount": "250", "callbackUrl": "https://webhook.site/020b8f72-82a9-4bf7-946a-137e12aa2532", "pin": "1pSF5lfWX+5lTX7RC1vNVLZZzoDg/vGZ9hm9JDp6ldFOLkM1", "shortCode": "800800", "msisdn": "254765555186", "remarks": "Merchant settlement payment" } 

#### **Response** 

Returns AcknowledgmentResponse — see Response Models. 

#### **Response Example** 

{ "responseCode": "0", "responseDesc": "Request accepted successfully", "serviceStatus": "PENDING" } 

### 2️⃣ **C2B PayBill Payment** 

**Endpoint:** POST /api/v1/transaction/c2b/paybill 

**Tags:** Payments API 

**Operation ID:** customerMerchantPaymentPaybill 

**Description:** Customer Pay Bill — Allows a customer to make a bill payment to a registered PayBill account. 

#### **Request Parameters** 

|**Field**|**Type**|**Required**|**Description**|||||||
|---|---|---|---|---|---|---|---|---|---|
|requestId|string|Yes|Unique request identifier. Must follow the format<br>shortCode_uniqueRequestId. Example:<br>247247_REQ123456654|||||||
|amount|string|Yes|Payment amount. Example:<br>250|||||||
|currency|string|Yes|ISO currency code. Pattern: `^(KES|UGX|TZS|RWF|SDG|CDF|ETB)$ .<br>Example:<br>KES`|
|billerCode|string|Yes|Registered PayBill number. Example:<br>800800|||||||
|billerName|string|Yes|Name of the biller/organization. Example:<br>DTOne|||||||
|pin|string|Yes|Merchant PIN encrypted as described in the<br>PIN &<br>Password Encryption section. Example:<br>2580|||||||
|callbackUrl|string|Yes|HTTPS endpoint for asynchronous status<br>notifications. Example:<br>https://yourapp.com/callback|||||||
|billPaymentReference|string|Yes|Customer reference number for the bill. Example:<br>TEST-REF-002|||||||
|msisdn|string|Yes|Customer mobile number in international format.<br>Example:<br>254765555186|||||||
|shortCode|string|No|Merchant short code. Example:<br>800800|||||||
|remarks|string|No||||||||



#### **Request Example** 

{ 

"requestId": "800800_80801234567890", 

"currency": "KES", 

"amount": "250", 

"callbackUrl": "https://webhook.site/020b8f72-82a9-4bf7-946a-137e12aa2532", 

- "pin": "2580", 

"shortCode": "800800", 

"billerCode": "800800", "msisdn": "254765555186", "billPaymentReference": "TEST-REF-002", "billerName": "DTOne", "remarks": "Merchant settlement payment" } 

#### **Response** 

Returns AcknowledgmentResponse — see Response Models. 

#### **Response Example** 

{ "responseCode": "0", "responseDesc": "Request accepted successfully", "serviceStatus": "PENDING" } 

## ✅ **C2B Validation** 

**Endpoint:** POST {partner_registered_validation_url} 

##### **Tags:** Callback API 

**Operation ID:** c2bValidation 

**Description:** Validates an incoming C2B payment request before processing. The partner must respond with a resultCode of 0 to accept the transaction, or 1 to reject it. 

### **Request Headers** 

|**Header**|**Value**|**Description**|
|---|---|---|
|Content-Type|application/json|Request body format|



### **Request Parameters** 

|**Field**|**Type**|**Required**|**Description**|
|---|---|---|---|
|transactionReference|string|Yes|Unique transaction reference identifier. Format:<br>shortCode_uniqueRequestId. Example:<br>247247_REF897986978|
|amount|string|Yes|Payment amount. Example:<br>1000|
|timestamp|string|Yes|Unix timestamp of the transaction initiation. Example:<br>78697698687|
|shortCode|string|Yes|Merchant short code or MSISDN identifier. Example:<br>msisdn|
|msisdn|string|Yes|Customer mobile number in international format. Example:<br>254765555186|
|firstName|string|Yes|First name of the customer making the payment. Example:<br>Joe|
|middleName|string|No|Middle name or initial of the customer. Example:<br>M|
|lastName|string|Yes|Last name of the customer making the payment. Example:<br>Doe|



### **Request Example** 

{ 

"transactionReference": "247247_REF897986978", 

"amount": "1000", 

"timestamp": "78697698687", 

"shortCode": "800800", 

"msisdn": "254764000000" 

"firstName": "Joe", 

"middleName": "M", 

"lastName": "Doe" 

} 

### **Response Parameters** 

|**Field**|**Type**|**Description**|
|---|---|---|
|resultCode|string|Result code of the validation.<br>0= Success (accept transaction),<br>1= Failed (reject transaction).|



|**Field**|**Type**|**Description**|
|---|---|---|
|resultDescription|string|Human-readable description of the result. Example:<br>SUCCESSor<br>FAILED.|
|thirdPartyTransactionRef|string|Echo back the original<br>transactionReferencefor correlation. Example:<br>247247_REF897986978|



### **Response Example — Accepted** 

{ 

"resultCode": "0", "resultDescription": "SUCCESS", 

"thirdPartyTransactionRef": "247247_REF897986978" 

} 

### **Response Example — Rejected** 

{ 

"resultCode": "1", "resultDescription": "FAILED", 

"thirdPartyTransactionRef": "247247_REF897986978" 

} 

### **Validation Logic** 

|resultCode|**Meaning**|**Action**|
|---|---|---|
|0|Transaction accepted|DFS proceeds to process the payment|
|1|Transaction rejected|DFS aborts the transaction and returns failure to the customer|



**Note:** The response must be returned within the platform's configured timeout (typically 10–30 seconds). A timeout or non200 HTTP response is treated as a rejection. 

## 📨 **C2B Confirmation** 

**Endpoint:** POST {partner_registered_confirmation_url} 

##### **Tags:** Callback API 

**Operation ID:** c2bConfirmation 

**Description:** Confirms the final status of a C2B transaction after processing is complete. This is a **one-way notification** — no response body is expected from the partner. The partner must return HTTP 200 OK to acknowledge receipt. 

### **Request Headers** 

|**Header**|**Value**|**Description**|
|---|---|---|
|Content-Type|application/json|Request body format|



### **Request Parameters** 

|**Field**|**Type**|**Required**|**Description**|
|---|---|---|---|
|transactionReference|string|Yes|Unique transaction reference identifier. Format:<br>shortCode_uniqueRequestId. Example:<br>247247_REF897986978|
|amount|string|Yes|Final payment amount. Example:<br>1000|
|timestamp|string|Yes|Unix timestamp of the transaction completion. Example:<br>42423422412|
|shortCode|string|Yes|Merchant short code. Example:<br>247247|
|msisdn|string|Yes|Customer mobile number in international format. Example:<br>254764000000|
|firstName|string|Yes|First name of the customer. Example:<br>Joe|
|middleName|string|No|Middle name or initial of the customer. Example:<br>M|
|lastName|string|Yes|Last name of the customer. Example:<br>Doe|



### **Request Example** 

{ "transactionReference": "247247_REF897986978", "amount": "1000", "timestamp": "42423422412", "shortCode": "247247", "msisdn": "254764000000", "firstName": "Joe", "middleName": "M", "lastName": "Doe" } 

### **Response** 

**No response body is required.** The partner must simply return HTTP 200 OK to acknowledge successful receipt of the confirmation. 

### **Acknowledgment** 

|**HTTP Status**|**Meaning**|
|---|---|
|200 OK|Confirmation received successfully|
|Any other status|System may retry delivery based on configured retry policy|



**Note:** Implement idempotency checks using transactionReference to avoid duplicate processing in case of retries. 

### 3️⃣ **Business-to-Customer Payment (B2C)** 

**Endpoint:** POST /api/v1/transaction/b2c/business-to-customer-payment 

**Tags:** Payments API 

**Operation ID:** businessToCustomerPayment 

**Description:** Initiates a disbursement from a merchant/organization account to a customer's mobile money wallet. 

#### **Request Parameters** 

|**Field**|**Type**|**Required**|**Description**|||||||
|---|---|---|---|---|---|---|---|---|---|
|requestId|string|Yes|Unique identifier for the request. Must follow the format<br>shortCode_uniqueRequestId. Example:<br>247247_REQ123456790|||||||
|amount|string|Yes|Payment amount. Example:<br>250|||||||
|currency|string|Yes|ISO currency code. Pattern: `^(KES|UGX|TZS|RWF|SDG|CDF|ETB)$ .<br>Example:<br>KES`|
|pin|string|Yes|Merchant PIN encrypted as described in the<br>PIN & Password<br>Encryption section. Example:<br>1pSF5lfWX+5lTX7RC1vNVLZZzoDg/vGZ9hm9JDp6ldFOLkM1|||||||
|shortCode|string|Yes|Merchant short code. Example:<br>800800|||||||
|msisdn|string|Yes|Customer mobile number in international format. Example:<br>254763555298|||||||
|callbackUrl|string|Yes|HTTPS endpoint for asynchronous status notifications.<br>Example:<br>https://yourapp.com/callback|||||||
|remarks|string|No||||||||



#### **Request Example** 

{ 

"requestId": "800800_80801234567890", 

"currency": "KES", 

"amount": "250", 

"pin": "1pSF5lfWX+5lTX7RC1vNVLZZzoDg/vGZ9hm9JDp6ldFOLkM1", 

"shortCode": "800800", 

"msisdn": "254763555298", 

"callbackUrl": "https://webhook.site/fd089a73-01cc-436f-b91c-0c6c547b243a", 

"remarks": "Merchant settlement payment" 

} 

**Response** 

Returns AcknowledgmentResponse — see Response Models. 

#### **Response Example** 

{ "responseCode": "0", "responseDesc": "Request accepted successfully", "serviceStatus": "PENDING" } 

### 4️⃣ **B2B Buy Goods** 

##### **Endpoint:** POST /api/v1/transaction/b2b/buy-goods 

##### **Tags:** Payments API 

**Operation ID:** b2bBuyGoods 

**Description:** Enables a business to make a Buy Goods payment to another merchant's till number. 

#### **Request Parameters** 

|**Field**|**Type**|**Required**|**Description**|||||||
|---|---|---|---|---|---|---|---|---|---|
|transactionReference|string|Yes|Unique transaction reference identifier.<br>Must follow the format<br>shortCode_uniqueRequestId. Example:<br>247247_80801234567890|||||||
|amount|string|Yes|Payment amount. Example:<br>1090|||||||
|currency|string|Yes|ISO currency code. Pattern: `^(KES|UGX|TZS|RWF|SDG|CDF|ETB)$ .<br>Example:<br>KES`|
|initiatorShortCode|string|Yes|Short code of the initiating organization.<br>Example:<br>123456|||||||



|**Field**|**Type**|**Required**|**Description**|
|---|---|---|---|
|tillNumber|string|Yes|Till number of the receiving merchant.<br>Example:<br>987654|
|referenceData|object|Yes|Additional reference information for the<br>transaction.|
|referenceData.invoiceNumber|string|Yes|Invoice number associated with the<br>payment. Example:<br>INV001|
|referenceData.customerName|string|Yes|Name of the customer. Example:<br>John Doe|
|referenceData.accountNumber|string|Yes|Account number for reconciliation.<br>Example:<br>ACC123456|
|referenceData.remarks|string|Yes|Payment remarks or description. Example:<br>Merchant settlement payment|
|receiverOrgShortCode|string|Yes|Short code of the receiving organization.<br>Example:<br>600999|
|password|string|Yes|Organization password encrypted as<br>described in the<br>PIN & Password<br>Encryption section. Example:<br>EncryptedPassword123|
|callbackUrl|string|Yes|HTTPS endpoint for asynchronous status<br>notifications. Example:<br>https://yourapp.com/callback1|



#### **Request Example** 

{ 

"transactionReference": "247247_80801234567890", 

"amount": "1090", 

"currency": "KES", 

"initiatorShortCode": "123456", 

"tillNumber": "987654", 

"invoiceNumber": "INV001", 

"customerName": "John Doe", 

"accountNumber": "ACC123456", 

"remarks": "Merchant settlement payment" "receiverOrgShortCode": "600999", "password": "EncryptedPassword123", "callbackUrl": "https://yourapp.com/callback1" } 

#### **Response** 

Returns AcknowledgmentResponse — see Response Models. 

#### **Response Example** 

{ 

"responseCode": "0", "responseDesc": "Request accepted successfully", 

"serviceStatus": "PENDING" 

} 

### 5️⃣ **B2B Pay Bill** 

**Endpoint:** POST /api/v1/transaction/b2b/pay-bill 

**Tags:** Payments API 

**Operation ID:** b2bPayBill 

**Description:** Enables a business to make a PayBill payment to another organization's biller account. 

#### **Request Parameters** 

|**Field**|**Type**|**Required**|**Description**|
|---|---|---|---|
|transactionReference|string|Yes|Unique transaction reference identifier. Must follow the format<br>shortCode_uniqueRequestId.<br>Example:<br>247247_80801234567890|
|amount|string|Yes|Payment amount. Example:<br>100|



|**Field**|**Type**|**Required**|**Description**|
|---|---|---|---|
|currency|string|Yes|ISO currency code. Pattern: `^(KES|
|initiatorShortCode|string|Yes|Short code of the initiating organization. Example:<br>800800|
|tillNumber|string|Yes|Till number or biller code of the receiving organization. Example:<br>987654|
|referenceData|object|Yes|Additional reference information for the transaction.|
|referenceData.invoiceNumber|string|Yes|Invoice number associated with the payment. Example:<br>INV001|
|referenceData.customerName|string|Yes|Name of the customer. Example:<br>John Doe|
|referenceData.accountNumber|string|Yes|Account number for reconciliation. Example:<br>ACC123456|
|referenceData.remarks|string|Yes|Payment remarks or description. Example:<br>Merchant settlement payment|
|receiverOrgShortCode|string|Yes|Short code of the receiving organization. Example:<br>555555|
|password|string|Yes|Organization password encrypted as described in the<br>PIN & Password Encryption section.<br>Example:<br>HNJX3tUWjtcIjxpuNfK+WQq92mahYXnfPkJy/M248jx6Lq3aUA9dkaZcK63EUIHJKmIsbOERnd3pxQ==|
|callbackUrl|string|Yes|HTTPS endpoint for asynchronous status notifications. Example:<br>https://webhook.site/2ca6e398-0b77-4d94-9d8c-d0da5fe8c1a4|



#### **Request Example** 

{ 

"transactionReference": "247247_80801234567890", "amount": "100", "currency": "KES", "initiatorShortCode": "800800", 

"tillNumber": "987654", 

"invoiceNumber": "INV001", "customerName": "John Doe", "accountNumber": "ACC123456", "remarks": "Merchant settlement payment" "receiverOrgShortCode": "555555", 

"password": "HNJX3tUWjtcIjxpuNfK+WQq92mahYXnfPkJy/M248jx6Lq3aUA9dkaZcK63EUIHJKmIsbOERnd3pxQ==", 

"callbackUrl": "https://webhook.site/2ca6e398-0b77-4d94-9d8c-d0da5fe8c1a4" 

} 

#### **Response** 

Returns AcknowledgmentResponse — see Response Models. 

#### **Response Example** 

{ "responseCode": "0", "responseDesc": "Request accepted successfully", "serviceStatus": "PENDING" } 

### 6️⃣ **Query Transaction Status** 

**Endpoint:** POST /api/v1/transaction/query-transaction-status 

##### **Tags:** Experience API 

**Operation ID:** queryTransactionStatus 

**Description:** Queries the current processing status of a previously initiated transaction. 

#### **Request Parameters** 

|**Field**|**Type**|**Required**|**Description**|
|---|---|---|---|
|transactionId|string|Yes|Unique identifier of the transaction. Example:<br>CCK3000HRT|



**Note:** The transactionId field is used to query the status. In some implementations, receiptNumber may also be accepted as an alternative identifier. 

#### **Request Example** 

{ 

"transactionId": "CCK3000HRT" 

} 

#### **Response** 

Returns GenericResponse — see Response Models. 

#### **Response Example — Failed Transaction** 

{ 

"message": null, 

"code": "3011", 

"metadata": { 

"requestReference": null, 

"transactionId": null, 

"status": "FAILED" 

} 

} 

#### **Response Example — Successful Transaction** 

{ 

"message": "Your request was successful.", 

"code": "00", 

"metadata": { 

"requestReference": "DF3801U7FA", 

"transactionId": "DF3801U7FA", 

"status": "SUCCESS" 

} 

} 

### 7️⃣ **Query Organization Balance** 

**Endpoint:** POST /api/v1/transaction/organization/balance 

##### **Tags:** Experience API 

**Operation ID:** queryBalance 

**Description:** Retrieves the organization's account balance for monitoring and reconciliation purposes. 

#### **Request Parameters** 

|**Field**|**Type**|**Required**|**Description**|
|---|---|---|---|
|requestId|string|Yes|Unique identifier for the balance check request. Must follow the format<br>shortCode_uniqueRequestId.<br>Example:<br>247247_REQ123456654|
|organizationUsername|string|Yes|Organization username provided by the service provider. Example:<br>DTONEAPIUSER|
|password|string|Yes|Organization password. Example:<br>hJ8*K23#J8UL)GS7|
|shortCode|string|Yes|Short code of the organization whose balance is being checked. Example:<br>800800|
|accountType|string|Yes|Organization account type. Example: Organization E-Money Account|



**Note:** In production environments, the password and organizationUsername fields should be encrypted as described in the PIN & Password <u>Encryption</u> section. The examples shown here use plain text values for testing purposes only. 

#### **Request Example** 

{ 

"requestId": "247247_REQ123456654", 

"organizationUsername": "DTONEAPIUSER", 

"password": "hJ8*K23#J8UL)GS7", 

"shortCode": "800800", 

"accountType": "Organization E-Money Account" 

} 

#### **Response** 

Returns GenericResponse — see Response Models. 

#### **Response Example** 

{ 

"message": "Your request was successful.", "code": "00", "metadata": { "accountStatus": "Active", "accountHolderId": "800800", "accountTypeId": "22013", "accountTypeAlias": "Organization Working Account", "reservedBalance": "0.00", "accountName": "DefaultAccount", "accountNo": "500000000110371555", "currentBalance": "0.00", "currency": "KES", "availableBalance": "0.00", "unclearedBalance": "0.00", "status": "SUCCESS" //COMPLETED, CANCELLED, DECLINED, REVERSED } } 

### 8️⃣ **Reverse Transaction** 

**Endpoint:** POST /api/v1/transaction/reverse-transaction 

##### **Tags:** Experience API 

**Operation ID:** reverseTransaction 

**Description:** Reverses a previously completed transaction. The reversal is identified by the original transaction's receipt number and the amount to be reversed. 

#### **Request Parameters** 

|**Field**|**Type**|**Required**|**Description**|
|---|---|---|---|
|receiptNumber|string|Yes|Unique identifier of the transaction to be reversed, typically the receipt number from the original<br>transaction. Example:<br>254765555108|
|amount|string|Yes|The amount to be reversed, which should match the amount of the original transaction. Example:<br>100|
|organizationUsername|string|Yes|Organization username provided by the service provider. Example:<br>DTONEAPIUSER|
|password|string|Yes|Organization password. Example:<br>hJ8*K23#J8UL)GS7|
|shortCode|string|Yes|Short code of the organization whose balance is being checked. Example:<br>800800|



#### **Request Example** 

{ "receiptNumber": "DF4401U8N2", "amount": "200", "organizationUsername": "TESTAPIUSER", "password": "V5JK1qxgO4C6x0N3TYFiJxyfH/7UhOew5uvp5VBbPnZOs7V1JdDliM6bVKRhXh4WodMMu7B2G7Sl8GlT", "shortCode": "800800" 

} 

#### **Response** 

Returns GenericResponse — see Response Models. 

#### **Response Example** 

{ 

"message": "Your request was successful.", "code": "00", "metadata": { "originalAmount": "50.00", "transactionStatus": "Completed", "receiptNumber": "DF2101U6H7", "status": "SUCCESS" 

} 

} 

### 9️⃣ **Mini Statement** 

##### **Endpoint:** POST /api/v1/transaction/statement 

##### **Tags:** Experience API 

**Operation ID:** miniStatement 

**Description:** Retrieves a mini statement showing recent transactions for an organization or agent account. 

#### **Request Parameters** 

|**Field**|**Type**|**Required**|**Description**|
|---|---|---|---|
|requestId|string|Yes|Unique identifier for the request. Must follow the format<br>shortCode_uniqueRequestId. Example:<br>247247_REQ123456654|
|orgOperatorUserName|string|Yes|Agent or operator identifier. Example:<br>TESTAPIUSER|
|accountType|string|Yes|Type of account to query. Example:<br>org_float|
|transHistoryMaxNo|number|Yes|Maximum number of transaction history records to return. Example:<br>10|
|pin|string|Yes|Agent PIN encrypted as described in the<br>PIN & Password Encryption section. Example:<br>2580|
|shortCode|string|Yes|Organization or agent short code. Example:<br>800800|



#### **Request Example** 

{ 

"requestId": "247247_REQ123456654", 

// "agentOperatorId": "DTONEAPIUSER", 

"accountType": "Organization E-Money Account", "transHistoryMaxNo": 10, 

"pin": "l6J2RjhGQL5KhaU9SqpUBOHzfYAdjDL/n3JaOeGq49ywmgXYWv8xUVCcgxK1KDSR4FzU2Ysc2xxbvSRr", 

"shortCode": "800800", 

"orgOperatorUserName": "TESTAPIUSER" 

} 

#### **Response** 

Returns GenericResponse — see Response Models. 

#### **Response Example** 

{ 

"message": "Your request was successful.", "code": "00", "metadata": { "transactions": [ { "transactionId": "TXN001", "amount": "250.00", "currency": "KES", "type": "CREDIT", "date": "2026-05-15T10:30:00Z", "description": "Customer payment" } ], "status": "SUCCESS" } } 

## 📦 **Response Models** 

### **AcknowledgmentResponse** 

Standard acknowledgment response for payment operations. 

|**Field**|**Type**|**Description**|
|---|---|---|
|responseCode|string|Numeric response code. Example:<br>0|
|responseDesc|string|Human-readable description. Example:<br>Request accepted successfully|
|serviceStatus|string|Enum:<br>PENDING,<br>SUCCESS,<br>FAILED. Example:<br>PENDING|



{ } 

"responseCode": "0", "responseDesc": "Request accepted successfully", "serviceStatus": "PENDING" 

### **GenericResponse** 

Generic response model used for query and reversal operations. 

|**Field**|**Type**|**Description**|
|---|---|---|
|message|string|Response message|
|code|string|Response code|
|metadata|object|Additional response data (key-value pairs)|



{ 

"message": "Operation completed successfully", 

"code": "0", 

"metadata": {} 

} 

## ⚙ **Error Handling** 

### **HTTP Status Codes** 

|**HTTP Status**|**Description**|
|---|---|
|200|Request processed successfully|
|401|Unauthorized — Invalid or missing JWT token|
|400|Bad Request — Invalid parameters or malformed JSON|
|404|Not Found — Endpoint or resource not found|
|500|Internal Server Error|



### **Common Response Codes** 

|**Code**|**Description**|**Resolution**|
|---|---|---|
|0|Success / Request acknowledged|No action required|
|00|Request successful|No action required|
|100|Invalid request data|Check request parameters and format|
|101|Unauthorized or invalid credentials|Verify JWT token is valid and not expired|
|102|Transaction not found|Verify<br>receiptNumberor<br>transactionIdexists|
|103|Internal processing error|Retry request or contact support|
|104|Timeout from downstream system|Retry request with exponential backoff|
|3011|Transaction failed / not found|Verify transaction identifier and retry|
|3013|Invalid currency unit|Ensure currency meets ISO 4217 standard|



### **Error Response Example (401 Unauthorized)** 

{ 

"message": "Unauthorized", 

"code": "101", 

"metadata": { 

} 

"error": "Invalid or expired JWT token" 



<!-- Start of picture text -->
}<br><!-- End of picture text -->

## 🔁 **Callback Notifications** 

Asynchronous callbacks are sent to the callbackUrl provided in C2B payment and PayBill requests. Callbacks contain the final transaction status and should be used to update your system records. 

### **Callback Payload Structure** 

|**Field**|**Type**|**Description**|
|---|---|---|
|transactionReference|string|Partner request reference identifier. Example:<br>REQ1725187|
|resultType|string|Transaction result type. Enum:<br>SUCCESS,<br>FAILED. Example:<br>SUCCESS|
|resultCode|string|Response code. Example:<br>00|
|resultDesc|string|Human-readable response description. Example:<br>Transaction processed successfully|
|transactionId|string|Unique DFS transaction identifier. Example:<br>TXN789654123|



### **Example Callback Payload (Success)** 

{ 

"transactionReference": "REQ1725187", 

"resultType": "SUCCESS", 

"resultCode": "00", 

"resultDesc": "Transaction processed successfully", 

"transactionId": "TXN789654123" 

} 

### **Example Callback Payload (Failure)** 

{ "code": "3013", "message": "The specified currency unit is incorrect and does not meet the ISO4217 standard.", "metadata": { "errorMessage": "The specified currency unit is incorrect and does not meet the ISO4217 standard.", "requestReference": "BENAR988T24", "status": "FAILED", "transactionId": "DF20000000" } } 

### **Callback Security** 

**HTTPS Only:** Callback URLs must use HTTPS in production. 

**Idempotency:** Implement idempotency checks using transactionId or transactionReference . 

- **Acknowledgment:** Return HTTP 200 OK to confirm receipt. 

## 🧪 **Testing Guidelines** 

### **Environment Setup** 

1. **Base URL:** Use https://uat.finserve.africa/momo-apis for UAT testing. 

2. **Headers:** Set Content-Type: application/json and include a valid JWT token. 

3. **Request IDs:** Generate unique requestId values for each request using the format shortCode_uniqueRequestId . 

### **Test Scenarios** 

|**Scenario**|**Test Data**|**Expected Result**|
|---|---|---|
|Successful C2B Payment|Valid<br>msisdn,<br>merchantCode|serviceStatus:<br>PENDING→ Callback:<br>SUCCESS|
|PayBill Payment|Valid<br>billerCode,<br>billPaymentReference|serviceStatus:<br>PENDING→ Callback:<br>SUCCESS|
|Reverse Transaction|Valid<br>receiptNumberof completed TXN|code:<br>0, reversal processed|



|**Scenario**|**Test Data**|**Expected Result**|
|---|---|---|
|Query Status|Valid<br>transactionId|metadata.statusreflects current state|
|Query Balance|Valid<br>organizationUsername,<br>password,<br>shortCode|metadata.balancereturned|
|Unauthorized|Missing/invalid JWT|HTTP<br>401|



### **Webhook Testing** 

Use tools like ngrok or Webhook.site to expose local endpoints for callback testing during development. 

## 📞 **Support & Contact** 

**Channel Details API Support** <u>support@finserve.africa</u> 

**Document Version:** 1.0.8 | **OpenAPI Version:** 3.1.0 | **Last Updated:** June 2026 | **Finserve Africa Limited** 

