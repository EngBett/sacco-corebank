#!/bin/bash

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
RED='\033[0;31m'
NC='\033[0m' # No Color

BASE_URL="http://localhost:9998"

echo -e "${BLUE}========================================${NC}"
echo -e "${BLUE}Testing C2B Register URL Flow${NC}"
echo -e "${BLUE}========================================${NC}\n"

# Step 1: Register C2B URL
echo -e "${GREEN}Step 1: Registering C2B URLs${NC}"
REGISTER_RESPONSE=$(curl -s -X POST "$BASE_URL/mpesa/c2b/v1/registerurl" \
  -H "Authorization: Bearer test_token" \
  -H "Content-Type: application/json" \
  -d '{
    "ShortCode": "600000",
    "ValidationURL": "http://localhost:5003/api/payments/c2b/validation",
    "ConfirmationURL": "http://localhost:5003/api/payments/c2b/confirmation",
    "ResponseType": "Completed"
  }')

echo "Response: $REGISTER_RESPONSE"
echo ""

# Step 2: Verify registration in database
echo -e "${GREEN}Step 2: Checking database for registration${NC}"
sqlite3 MockedMpesa.API/mockedmpesa.db "SELECT * FROM C2BRegistrations;" | head -5
echo ""

# Step 3: Trigger an STK Push (this should trigger C2B confirmation after successful callback)
echo -e "${GREEN}Step 3: Triggering STK Push (will send C2B confirmation to registered URL)${NC}"
echo "Note: This will send both STK callback AND C2B confirmation callback"
echo ""

STK_RESPONSE=$(curl -s -X POST "$BASE_URL/mpesa/stkpush/v1/processrequest" \
  -H "Authorization: Bearer test_token" \
  -H "Content-Type: application/json" \
  -d '{
    "BusinessShortCode": "600000",
    "Password": "test",
    "Timestamp": "20240119161645",
    "TransactionType": "CustomerPayBillOnline",
    "Amount": "1000",
    "PartyA": "254712345678",
    "PartyB": "600000",
    "PhoneNumber": "254712345678",
    "CallBackURL": "http://host.docker.internal:5010/api/payments/webhooks/demo/mpesa",
    "AccountReference": "Invoice123",
    "TransactionDesc": "Test Payment"
  }')

echo "STK Push Response: $STK_RESPONSE"
echo ""
echo -e "${BLUE}Wait 5 seconds for callbacks to be sent...${NC}"
sleep 5

# Step 4: Check transaction in database
echo -e "\n${GREEN}Step 4: Checking transaction in database${NC}"
sqlite3 MockedMpesa.API/mockedmpesa.db "SELECT MerchantRequestId, MpesaReceiptNumber, CallbackStatus FROM Transactions ORDER BY CreatedAt DESC LIMIT 1;"
echo ""

echo -e "${BLUE}========================================${NC}"
echo -e "${BLUE}Expected Results:${NC}"
echo -e "${BLUE}1. C2B URLs registered successfully${NC}"
echo -e "${BLUE}2. STK Push callback sent to CallBackURL${NC}"
echo -e "${BLUE}3. C2B Confirmation sent to ConfirmationURL${NC}"
echo -e "${BLUE}   - TransID = MpesaReceiptNumber${NC}"
echo -e "${BLUE}   - Amount = 1000.00${NC}"
echo -e "${BLUE}   - BillRefNumber = Invoice123${NC}"
echo -e "${BLUE}========================================${NC}"
